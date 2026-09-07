// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public enum WinPeMountState
{
    Mounting,
    Mounted,
    Unmounted,
    RecoveryRequired
}

public static class WinPeMountRecovery
{
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(2);

    public static async Task<WinPeResult> ReconcileOwnedMountAsync(
        IWinPeProcessRunner runner, string dismPath, string bootWimPath, string mountDirectoryPath,
        string workingDirectory, CancellationToken cleanupToken, bool nativeTerminationConfirmed = false)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cleanupToken);
        bounded.CancelAfter(CleanupTimeout);
        cleanupToken = bounded.Token;
        WinPeResult<bool> inventory = await InspectAsync(runner, dismPath, bootWimPath, mountDirectoryPath,
            workingDirectory, cleanupToken).ConfigureAwait(false);
        if (!inventory.IsSuccess)
        {
            return WinPeResult.Failure(WithOwnership(inventory.Error!, bootWimPath, mountDirectoryPath,
                nativeTerminationConfirmed && (inventory.Error!.Exception is null || IsTerminationConfirmed(inventory.Error.Exception))));
        }
        if (!nativeTerminationConfirmed)
        {
            return WinPeResult.Failure(WithOwnership(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "Native termination is unconfirmed; mount resources must be retained."), bootWimPath, mountDirectoryPath, false));
        }
        if (!inventory.Value)
        {
            return WinPeResult.Success();
        }
        try
        {
            WinPeProcessExecution discard = await runner.RunAsync(dismPath,
                ["/English", "/Unmount-Image", $"/MountDir:{mountDirectoryPath}", "/Discard"], workingDirectory,
                cleanupToken, executionTimeout: CleanupTimeout).ConfigureAwait(false);
            if (discard.IsSuccess)
            {
                return WinPeResult.Success();
            }
            return WinPeResult.Failure(WithOwnership(discard.ToFailureDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "Failed to discard the owned mounted image."), bootWimPath, mountDirectoryPath, true));
        }
        catch (Exception ex)
        {
            return WinPeResult.Failure(WithOwnership(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "Failed to reconcile the owned mounted image.", exception: ex), bootWimPath, mountDirectoryPath,
                IsTerminationConfirmed(ex)));
        }
    }

    internal static async Task<WinPeResult<bool>> InspectAsync(IWinPeProcessRunner runner, string dismPath,
        string imagePath, string mountPath, string workingDirectory, CancellationToken token)
    {
        try
        {
            WinPeProcessExecution execution = await runner.RunAsync(dismPath, ["/English", "/Get-MountedWimInfo"],
                workingDirectory, token, executionTimeout: CleanupTimeout).ConfigureAwait(false);
            if (!execution.IsSuccess)
            {
                return WinPeResult<bool>.Failure(execution.ToFailureDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                    "Failed to read the mounted image inventory."));
            }
            execution.EnsureCompleteOutput();
            return WinPeResult<bool>.Success(ParseOwnedMount(execution.StandardOutput, imagePath, mountPath));
        }
        catch (Exception ex)
        {
            return WinPeResult<bool>.Failure(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "The mounted image inventory could not confirm ownership.", exception: ex));
        }
    }

    /// <summary>Validates a complete English DISM inventory and resolves one exact owned image/mount pair.</summary>
    /// <exception cref="InvalidDataException">Inventory is incomplete, ambiguous, or conflicts with the owned pair.</exception>
    public static bool ParseOwnedMount(string output, string imagePath, string mountPath)
    {
        if (output.Length > 1024 * 1024)
            throw new InvalidDataException("Mounted image inventory exceeds the supported size.");
        string[] lines = output.Split('\n');
        if (lines.Length > 16384 || lines.Any(line => line.Length > 8192))
            throw new InvalidDataException("Mounted image inventory exceeds the supported line limits.");
        bool header = false, completed = false, found = false;
        Dictionary<string, string>? entry = null;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void FinishEntry()
        {
            if (entry is null) return;
            if (!entry.TryGetValue("Mount Dir", out string? directory) ||
                !entry.TryGetValue("Image File", out string? image) ||
                !entry.TryGetValue("Image Index", out string? index) || !int.TryParse(index, out int parsedIndex) || parsedIndex < 1 ||
                !entry.TryGetValue("Mounted Read/Write", out string? writable) || writable is not ("Yes" or "No") ||
                !entry.TryGetValue("Status", out string? status) || status is not ("Ok" or "OK" or "Needs Remount" or "Invalid"))
                throw new InvalidDataException("Incomplete mounted image inventory record.");
            string canonicalMount = CanonicalPath(directory);
            string canonicalImage = CanonicalPath(image);
            if (!paths.Add(canonicalMount)) throw new InvalidDataException("Duplicate mounted image path.");
            bool sameImage = canonicalImage.Equals(CanonicalPath(imagePath), StringComparison.OrdinalIgnoreCase);
            bool sameMount = canonicalMount.Equals(CanonicalPath(mountPath), StringComparison.OrdinalIgnoreCase);
            if (sameImage && !sameMount)
                throw new InvalidDataException("The owned image is mounted at another path and must be retained.");
            if (sameMount)
            {
                if (!sameImage)
                    throw new InvalidDataException("The owned mount path belongs to a different image.");
                found = true;
            }
            entry = null;
        }
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (!header)
            {
                if (line == "Mounted images:") header = true;
                continue;
            }
            if (completed) throw new InvalidDataException("Unexpected content after mounted image inventory.");
            if (line == "The operation completed successfully.")
            {
                FinishEntry();
                completed = true;
                continue;
            }
            int separator = line.IndexOf(':');
            if (separator < 0) throw new InvalidDataException("Unrecognized mounted image inventory line.");
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (key == "Mount Dir")
            {
                FinishEntry();
                entry = new(StringComparer.Ordinal);
            }
            if (entry is null || key is not ("Mount Dir" or "Image File" or "Image Index" or "Mounted Read/Write" or "Status") ||
                !entry.TryAdd(key, value)) throw new InvalidDataException("Unrecognized mounted image inventory field.");
        }
        if (!header || !completed) throw new InvalidDataException("Incomplete mounted image inventory response.");
        return found;
    }

    private static string CanonicalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Mounted image inventory paths must be absolute.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static bool IsTerminationConfirmed(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current.Data.Contains("ProcessRootExitConfirmed") || current.Data.Contains("ProcessTreeTerminationConfirmed") ||
                current.Data.Contains("ProcessOutputDrainConfirmed") || current is OperationCanceledException or TimeoutException)
                return current.Data["ProcessRootExitConfirmed"] is true && current.Data["ProcessTreeTerminationConfirmed"] is true;
        }
        return true;
    }

    internal static WinPeDiagnostic WithOwnership(WinPeDiagnostic error, string imagePath, string mountPath, bool terminationConfirmed) =>
        error with
        {
            RecoveryRequired = true,
            OwnedImagePath = imagePath,
            OwnedMountPath = mountPath,
            RetainedPaths = [mountPath, imagePath],
            NativeTerminationConfirmed = terminationConfirmed
        };

    internal static WinPeDiagnostic Combine(WinPeDiagnostic primary, WinPeResult cleanup) => cleanup.IsSuccess ? primary : primary with
    {
        CleanupDiagnostic = cleanup.Error,
        RecoveryRequired = primary.RecoveryRequired || cleanup.Error!.RecoveryRequired,
        OwnedMountPath = cleanup.Error!.OwnedMountPath ?? primary.OwnedMountPath,
        OwnedImagePath = cleanup.Error.OwnedImagePath ?? primary.OwnedImagePath,
        RetainedPaths = primary.RetainedPaths.Concat(cleanup.Error.RetainedPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        NativeTerminationConfirmed = cleanup.Error.NativeTerminationConfirmed,
        Details = string.Join(Environment.NewLine, primary.Details, "Discard diagnostics:", cleanup.Error.Details ?? cleanup.Error.Message)
    };
}
