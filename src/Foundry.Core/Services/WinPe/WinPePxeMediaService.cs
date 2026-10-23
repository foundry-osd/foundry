// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Writes the UEFI network boot files of a prepared WinPE workspace: the boot image, the RAM disk device image,
/// the boot configuration store and the boot manager.
/// </summary>
public sealed class WinPePxeMediaService : IWinPePxeMediaService
{
    private const string PendingSuffix = ".foundry-pending";
    private const string ValidateStage = "Validate PXE output";
    private const string CopyStage = "Copy PXE boot files";

    private readonly Func<string, long> availableBytes;

    /// <summary>Gets the names of the files written to the output folder.</summary>
    public static IReadOnlyList<string> OutputFileNames { get; } = ["boot.wim", "boot.sdi", "BCD", "bootmgr.efi"];

    public WinPePxeMediaService()
        : this(WindowsVolumeStorage.GetAvailableBytes)
    {
    }

    internal WinPePxeMediaService(Func<string, long> availableBytes)
    {
        this.availableBytes = availableBytes;
    }

    /// <inheritdoc />
    public async Task<WinPeResult> CreateAsync(WinPePxeMediaOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.PreparedWorkspace is null || string.IsNullOrWhiteSpace(options.OutputDirectoryPath))
        {
            return WinPeResult.Failure(WinPeErrorCodes.ValidationFailed,
                "A prepared workspace and an output folder are required to write PXE boot files.");
        }

        ReportProgress(options.Progress, 0, WinPeProgressStatuses.PreparingPxeOutput);
        IReadOnlyList<(string Source, string Name)> files = ResolveSources(options.PreparedWorkspace);
        string? missing = files.Select(file => file.Source).FirstOrDefault(source => !File.Exists(source));
        if (missing is not null)
        {
            return WinPeResult.Failure(WinPeErrorCodes.PxeOutputFailed,
                "A boot file required for PXE output was not found in the prepared workspace.", missing,
                stage: ValidateStage, failureKind: WinPeFailureKinds.FileSystem, failureReason: WinPeFailureReasons.ArtifactMissing);
        }

        string outputDirectory = Path.GetFullPath(options.OutputDirectoryPath.Trim());
        List<string> pendingPaths = [];
        try
        {
            Directory.CreateDirectory(outputDirectory);
            long requiredBytes = files.Sum(file => new FileInfo(file.Source).Length);
            long available = availableBytes(outputDirectory);
            if (available < requiredBytes)
            {
                return WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.PxeOutputCapacityInsufficient,
                    "There is insufficient free space in the PXE output folder.", outputDirectory, stage: ValidateStage,
                    failureKind: WinPeFailureKinds.Validation, failureReason: WinPeFailureReasons.InvalidInput)
                {
                    RequiredBytes = (ulong)requiredBytes,
                    AvailableBytes = (ulong)Math.Max(0, available),
                    StoragePath = outputDirectory
                });
            }

            // Every file is written under a pending name first so a failure never mixes old and new boot files.
            long copiedBytes = 0;
            foreach ((string source, string name) in files)
            {
                string pendingPath = Path.Combine(outputDirectory, name + PendingSuffix);
                pendingPaths.Add(pendingPath);
                await CopyFileAsync(source, pendingPath, cancellationToken).ConfigureAwait(false);
                copiedBytes += new FileInfo(source).Length;
                ReportProgress(options.Progress, 5 + (int)(copiedBytes * 90 / Math.Max(1, requiredBytes)), WinPeProgressStatuses.CopyingPxeFiles);
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach ((string _, string name) in files)
            {
                File.Move(Path.Combine(outputDirectory, name + PendingSuffix), Path.Combine(outputDirectory, name), overwrite: true);
            }

            ReportProgress(options.Progress, 100, WinPeProgressStatuses.PxeOutputCompleted);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WinPeResult.Failure(WinPeErrorCodes.PxeOutputFailed,
                "The PXE boot files could not be written to the output folder.", ex.ToString(), stage: CopyStage,
                failureKind: WinPeFailureKinds.FileSystem,
                failureReason: ex is UnauthorizedAccessException ? WinPeFailureReasons.AccessDenied : WinPeFailureReasons.IoError,
                exception: ex);
        }
        finally
        {
            DeletePendingFiles(pendingPaths);
        }
    }

    private static IReadOnlyList<(string Source, string Name)> ResolveSources(WinPeWorkspacePreparationResult workspace)
    {
        string media = workspace.Artifact.MediaDirectoryPath;
        // MakeWinPEMedia selects the same boot manager for the 2011 or 2023 signature, so Secure Boot trusts the network start
        // exactly as it trusts the ISO built from this workspace.
        string bootManager = Path.Combine(workspace.Artifact.WorkingDirectoryPath, "bootbins",
            workspace.UseBootEx ? "bootmgfw_EX.efi" : "bootmgfw.efi");
        return
        [
            (Path.Combine(media, "sources", "boot.wim"), "boot.wim"),
            (Path.Combine(media, "Boot", "boot.sdi"), "boot.sdi"),
            (Path.Combine(media, "EFI", "Microsoft", "Boot", "BCD"), "BCD"),
            (bootManager, "bootmgr.efi")
        ];
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeletePendingFiles(IEnumerable<string> pendingPaths)
    {
        foreach (string path in pendingPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup: a pending file is never read as a boot file.
            }
        }
    }

    private static void ReportProgress(IProgress<WinPeMediaProgress>? progress, int percent, string status)
    {
        progress?.Report(new WinPeMediaProgress { Percent = Math.Clamp(percent, 0, 100), Status = status });
    }
}
