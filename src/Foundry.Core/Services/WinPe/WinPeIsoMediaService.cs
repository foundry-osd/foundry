// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeIsoMediaService : IWinPeIsoMediaService
{
    private readonly IWinPeProcessRunner _processRunner;
    internal Func<Stream, Stream, CancellationToken, Task> CopyIsoAsync { get; init; } =
        (source, destination, token) => source.CopyToAsync(destination, token);
    internal Action<string, string, string> ReplaceIso { get; init; } = File.Replace;
    internal Action<string> DeleteIso { get; init; } = File.Delete;

    public WinPeIsoMediaService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeIsoMediaService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult> CreateAsync(
        WinPeIsoMediaOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeWorkspacePreparationResult preparedWorkspace = options.PreparedWorkspace!;
        string requestedOutputPath = options.OutputIsoPath.Trim();
        string? preparedOutputPath = null;
        string? safeWorkspacePath = null;
        string currentStage = "Prepare ISO output path";
        bool retainWorkspace = false;

        try
        {
            WinPeProcessRunner.ValidateCmdScript(
                preparedWorkspace.Tools.MakeWinPeMediaPath,
                $"/ISO /F {WinPeProcessRunner.Quote(preparedWorkspace.Artifact.WorkingDirectoryPath)} {WinPeProcessRunner.Quote(requestedOutputPath)}");
            if (ContainsNonAscii(requestedOutputPath) || ContainsNonAscii(preparedWorkspace.Artifact.WorkingDirectoryPath))
            {
                _ = WinPeProcessRunner.Quote(options.IsoTempDirectoryPath);
            }

            ReportProgress(options.Progress, 0, "Preparing ISO output path.");
            EnsureOutputDirectoryExists(requestedOutputPath);
            if (!options.ForceOverwriteOutput && File.Exists(requestedOutputPath))
            {
                throw new IOException("An ISO already exists at the selected output path.");
            }
            currentStage = "Prepare ISO workspace";
            ReportProgress(options.Progress, 20, "Preparing ISO workspace.");
            string makeWinPeMediaWorkspacePath = PrepareWorkspacePath(
                preparedWorkspace.Artifact.WorkingDirectoryPath,
                options.IsoTempDirectoryPath,
                out safeWorkspacePath);

            preparedOutputPath = Path.Combine(makeWinPeMediaWorkspacePath, $"iso-{Guid.NewGuid():N}.iso");

            string arguments =
                $"/ISO /F {WinPeProcessRunner.Quote(makeWinPeMediaWorkspacePath)} {WinPeProcessRunner.Quote(preparedOutputPath)}" +
                (preparedWorkspace.UseBootEx ? " /bootex" : string.Empty);

            currentStage = "Run MakeWinPEMedia for ISO";
            ReportProgress(options.Progress, 40, "Running MakeWinPEMedia for ISO.");
            WinPeProcessExecution execution = await _processRunner.RunCmdScriptAsync(
                preparedWorkspace.Tools.MakeWinPeMediaPath,
                arguments,
                makeWinPeMediaWorkspacePath,
                cancellationToken).ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                return WinPeResult.Failure(execution.ToFailureDiagnostic(
                    WinPeErrorCodes.IsoCreateFailed,
                    "Failed to create WinPE ISO media.",
                    "Create ISO media",
                    "MakeWinPEMedia"));
            }

            if (!File.Exists(preparedOutputPath) || new FileInfo(preparedOutputPath).Length == 0)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.IsoCreateFailed,
                    "MakeWinPEMedia completed without producing the expected ISO artifact.",
                    execution.ToDiagnosticText(),
                    stage: "Create ISO media",
                    failureKind: WinPeFailureKinds.Process,
                    failureReason: WinPeFailureReasons.ArtifactMissing,
                    toolName: "MakeWinPEMedia",
                    errorSummary: "Expected ISO artifact was not produced.");
            }

            currentStage = "Finalize ISO output";
            ReportProgress(options.Progress, 90, "Finalizing ISO output.");
            WinPeDiagnostic? cleanup = await FinalizeOutputAsync(
                preparedOutputPath, requestedOutputPath, options.ForceOverwriteOutput, cancellationToken).ConfigureAwait(false);
            ReportProgress(options.Progress, 100, "ISO media completed.");
            return WinPeResult.SuccessWithCleanup(cleanup);
        }
        catch (Exception ex)
        {
            bool writerUncertain = currentStage == "Run MakeWinPEMedia for ISO" &&
                (ex.Data["ProcessRootExitConfirmed"] is false || ex.Data["ProcessTreeTerminationConfirmed"] is false ||
                 ((ex is TimeoutException or OperationCanceledException) &&
                  !(ex.Data["ProcessRootExitConfirmed"] is true && ex.Data["ProcessTreeTerminationConfirmed"] is true)));
            retainWorkspace = writerUncertain || ex.Data["PublicationRecoveryRequired"] is true;
            if (ex is OperationCanceledException && !retainWorkspace) throw;
            WinPeResult failure = WinPeResult.Failure(
                WinPeErrorCodes.IsoCreateFailed,
                "Failed to create WinPE ISO media.",
                ex.ToString(),
                stage: currentStage,
                failureKind: currentStage == "Run MakeWinPEMedia for ISO"
                    ? WinPeFailureKinds.Process
                    : WinPeFailureKinds.FileSystem,
                failureReason: ex switch
                {
                    OperationCanceledException => WinPeFailureReasons.Cancelled,
                    TimeoutException when currentStage == "Run MakeWinPEMedia for ISO" => WinPeFailureReasons.Timeout,
                    UnauthorizedAccessException => WinPeFailureReasons.AccessDenied,
                    IOException => WinPeFailureReasons.IoError,
                    _ when currentStage == "Run MakeWinPEMedia for ISO" => WinPeFailureReasons.ProcessStartFailed,
                    _ => WinPeFailureReasons.Unexpected
                },
                toolName: currentStage == "Run MakeWinPEMedia for ISO" ? "MakeWinPEMedia" : null,
                errorSummary: ex.Message,
                exception: ex);
            return WinPeResult.Failure(failure.Error! with
            {
                RecoveryRequired = retainWorkspace || ex.Data["PublicationRecoveryRequired"] is true,
                RetainedPaths = (retainWorkspace
                    ? new[] { safeWorkspacePath ?? preparedWorkspace.Artifact.WorkingDirectoryPath }
                    : []).Concat(ex.Data["PublicationRetainedPaths"] as string[] ?? []).ToArray(),
                NativeTerminationConfirmed = !writerUncertain
            });
        }
        finally
        {
            if (!retainWorkspace)
            {
                CleanupPreparedOutput(requestedOutputPath, preparedOutputPath);
                CleanupPreparedWorkspace(safeWorkspacePath);
            }
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeIsoMediaOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO media options are required.",
                "Provide a non-null WinPeIsoMediaOptions instance.");
        }

        if (options.PreparedWorkspace is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace is required.",
                "Set WinPeIsoMediaOptions.PreparedWorkspace.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Tools.MakeWinPeMediaPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "MakeWinPEMedia path is required.",
                "Set WinPeToolPaths.MakeWinPeMediaPath.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Artifact.WorkingDirectoryPath) ||
            !Directory.Exists(options.PreparedWorkspace.Artifact.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace directory was not found.",
                $"Path: '{options.PreparedWorkspace.Artifact.WorkingDirectoryPath}'.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputIsoPath) ||
            !options.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Output ISO path must end with .iso.",
                $"Path: '{options.OutputIsoPath}'.");
        }

        if ((ContainsNonAscii(options.OutputIsoPath) ||
             ContainsNonAscii(options.PreparedWorkspace.Artifact.WorkingDirectoryPath)) &&
            string.IsNullOrWhiteSpace(options.IsoTempDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO temporary directory path is required for non-ASCII MakeWinPEMedia paths.",
                "Set WinPeIsoMediaOptions.IsoTempDirectoryPath.");
        }

        return null;
    }

    private static string PrepareWorkspacePath(
        string requestedWorkspacePath,
        string isoTempDirectoryPath,
        out string? safeWorkspacePath)
    {
        safeWorkspacePath = null;
        if (!ContainsNonAscii(requestedWorkspacePath))
        {
            return requestedWorkspacePath;
        }

        Directory.CreateDirectory(isoTempDirectoryPath);
        safeWorkspacePath = Path.Combine(isoTempDirectoryPath, $"workspace-{Guid.NewGuid():N}");
        CopyDirectoryContents(requestedWorkspacePath, safeWorkspacePath);
        return safeWorkspacePath;
    }


    private async Task<WinPeDiagnostic?> FinalizeOutputAsync(
        string preparedOutputPath, string requestedOutputPath, bool overwrite, CancellationToken cancellationToken)
    {
        string target = Path.GetFullPath(requestedOutputPath);
        string sibling = target + $".staged-{Guid.NewGuid():N}";
        string backup = target + $".previous-{Guid.NewGuid():N}";
        bool retainSibling = false;
        try
        {
            await using FileStream source = new(preparedOutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = source.Length;
            byte[] hash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
            source.Position = 0;
            await using (FileStream destination = new(sibling, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await CopyIsoAsync(source, destination, cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                destination.Position = 0;
                byte[] copiedHash = await SHA256.HashDataAsync(destination, cancellationToken).ConfigureAwait(false);
                if (destination.Length != length ||
                    !hash.AsSpan().SequenceEqual(copiedHash))
                {
                    throw new InvalidDataException("The staged ISO copy did not match the completed artifact.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (overwrite && File.Exists(target))
            {
                try
                {
                    ReplaceIso(sibling, target, backup);
                }
                catch (Exception ex)
                {
                    retainSibling = true;
                    ex.Data["PublicationRecoveryRequired"] = true;
                    ex.Data["PublicationRetainedPaths"] = new[] { sibling, backup }.Where(File.Exists).ToArray();
                    throw;
                }
            }
            else
            {
                File.Move(sibling, target, overwrite: false);
            }

            try
            {
                if (File.Exists(backup)) DeleteIso(backup);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new WinPeDiagnostic(WinPeErrorCodes.IsoCreateFailed,
                    "ISO publication succeeded, but its previous output could not be removed.", exception: ex)
                {
                    RetainedPaths = [backup]
                };
            }
        }
        finally
        {
            if (!retainSibling) CleanupPreparedOutput(target, sibling);
        }
    }
    private static void CleanupPreparedOutput(string requestedOutputPath, string? preparedOutputPath)
    {
        if (string.IsNullOrWhiteSpace(preparedOutputPath) ||
            string.Equals(preparedOutputPath, requestedOutputPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(preparedOutputPath))
        {
            return;
        }

        try
        {
            File.Delete(preparedOutputPath);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void CleanupPreparedWorkspace(string? safeWorkspacePath)
    {
        if (string.IsNullOrWhiteSpace(safeWorkspacePath) || !Directory.Exists(safeWorkspacePath))
        {
            return;
        }

        try
        {
            Directory.Delete(safeWorkspacePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void CopyDirectoryContents(string sourceDirectoryPath, string targetDirectoryPath)
    {
        Directory.CreateDirectory(targetDirectoryPath);

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetDirectoryPath, relativePath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
            string targetFilePath = Path.Combine(targetDirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(filePath, targetFilePath, overwrite: true);
        }
    }

    private static void EnsureOutputDirectoryExists(string outputIsoPath)
    {
        string? outputDirectory = Path.GetDirectoryName(outputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static bool ContainsNonAscii(string value)
    {
        return value.Any(character => character > 127);
    }

    private static void ReportProgress(IProgress<WinPeMediaProgress>? progress, int percent, string status)
    {
        progress?.Report(new WinPeMediaProgress
        {
            Percent = Math.Clamp(percent, 0, 100),
            Status = status
        });
    }

}
