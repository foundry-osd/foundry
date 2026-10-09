// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Serilog;

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeWorkspaceCleanupService
{
    // A marker holds a single mount path, and Windows limits a path to 32,767 UTF-16 code units.
    private const long MaxCleanupMarkerBytes = 128 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Resolves the cleanup markers that <see cref="WinPeMountSession"/> retained when a DISM discard had no
    /// confirmed exit, so that a media operation is not blocked forever once Windows no longer holds the mount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A marker means that a discard may still be running against the mount directory it names, which is why
    /// <see cref="Delete"/> and <see cref="EnsureServicingCanStart"/> refuse to proceed while it exists. This pass
    /// removes a marker only on evidence from Windows that the mount is gone: either the mounted-image inventory
    /// does not report the marker's mount directory, or one DISM discard bounded by
    /// <see cref="WinPeMountSession.CleanupTimeout"/> returns success and a fresh inventory read no longer reports
    /// it. In every other case the marker is kept and servicing stays blocked: the inventory cannot be read, the
    /// discard fails, times out or cannot be confirmed, or the marker does not name a mount directory inside its own
    /// operation.
    /// </para>
    /// <para>
    /// Only operations that Foundry owns and whose lease can be opened exclusively are examined, so an operation that
    /// is still running in another Foundry instance is never touched. The lease is held for as long as the markers
    /// of that operation are examined. Reparse points are never followed.
    /// </para>
    /// <para>
    /// The pass removes markers only and never deletes a workspace. Call it before stale-workspace deletion and
    /// before <see cref="EnsureServicingCanStart"/>: <see cref="DeleteOwnedOperation"/> can then remove a recovered
    /// workspace after its own inventory check, and the servicing check reports whatever remains unresolved. It does
    /// not throw, because that check fails closed on any marker left behind.
    /// </para>
    /// </remarks>
    /// <param name="workspaceRoot">Directory whose immediate children are operation workspaces.</param>
    /// <param name="dismPath">DISM executable from <see cref="WinPeToolPaths.DismPath"/>, as used to mount images.</param>
    public async Task RecoverUnresolvedMountCleanupsAsync(string workspaceRoot, string dismPath)
    {
        ILogger logger = Log.ForContext<WinPeWorkspaceCleanupService>();
        string root;
        string[] candidates;
        try
        {
            string? inspectableRoot = ResolveInspectableRoot(workspaceRoot);
            if (inspectableRoot is null) return;
            root = inspectableRoot;
            candidates = Directory.GetDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.Warning(exception,
                "Retained mounted-image cleanup could not be inspected for recovery. WorkspaceRoot={WorkspaceRoot}",
                workspaceRoot);
            return;
        }

        foreach (string candidate in candidates)
        {
            try
            {
                await RecoverOperationAsync(root, candidate, dismPath, logger).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.Warning(exception,
                    "Retained mounted-image cleanup could not be recovered because the operation could not be safely inspected. OperationPath={OperationPath}",
                    candidate);
            }
        }
    }

    private async Task RecoverOperationAsync(string root, string candidate, string dismPath, ILogger logger)
    {
        string? operationPath = ResolveOwnedOperation(root, candidate);
        if (operationPath is null || !EnumerateCleanupMarkers(operationPath).Any()) return;

        FileStream lease;
        try
        {
            lease = AcquireInactiveOperationLease(root, operationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.Warning(exception,
                "Retained mounted-image cleanup was left untouched because ownership or inactivity of the operation could not be established. OperationPath={OperationPath}",
                operationPath);
            return;
        }

        using (lease)
        {
            // A mount gets at most one discard attempt per pass, even when several markers name it.
            var attemptedMounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string markerPath in EnumerateCleanupMarkers(operationPath).ToArray())
            {
                await RecoverMarkerAsync(operationPath, markerPath, dismPath, attemptedMounts, logger).ConfigureAwait(false);
            }
        }
    }

    private async Task RecoverMarkerAsync(
        string operationPath,
        string markerPath,
        string dismPath,
        HashSet<string> attemptedMounts,
        ILogger logger)
    {
        string? mountDirectory = TryReadMarkerMountDirectory(operationPath, markerPath);
        if (mountDirectory is null)
        {
            LogUnresolved(logger, null, operationPath, null, markerPath,
                "The marker does not name a mount directory inside its operation.");
            return;
        }

        bool? isMounted = IsImageMounted(mountDirectory, out Exception? inventoryFailure);
        if (isMounted is null)
        {
            LogUnresolved(logger, inventoryFailure, operationPath, mountDirectory, markerPath,
                "The mounted-image inventory could not be read.");
            return;
        }

        if (isMounted == false)
        {
            RemoveVerifiedMarker(logger, operationPath, mountDirectory, markerPath, "ImageNotMounted");
            return;
        }

        if (!attemptedMounts.Add(mountDirectory))
        {
            LogUnresolved(logger, null, operationPath, mountDirectory, markerPath,
                "The image is still mounted and its discard was already attempted in this pass.");
            return;
        }

        if (string.IsNullOrWhiteSpace(dismPath))
        {
            LogUnresolved(logger, null, operationPath, mountDirectory, markerPath,
                "The image is still mounted and no DISM executable was supplied.");
            return;
        }

        WinPeProcessExecution discard;
        using (var cleanup = new CancellationTokenSource(WinPeMountSession.CleanupTimeout, _timeProvider))
        {
            try
            {
                discard = await WinPeMountSession.RunDiscardAsync(
                    _processRunner,
                    dismPath,
                    mountDirectory,
                    Path.GetDirectoryName(markerPath)!,
                    cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogUnresolved(logger, exception, operationPath, mountDirectory, markerPath,
                    cleanup.IsCancellationRequested
                        ? $"The DISM discard retry exceeded the cleanup deadline of {WinPeMountSession.CleanupTimeout}."
                        : "The DISM discard retry did not return a result.");
                return;
            }
        }

        if (!discard.IsSuccess)
        {
            LogUnresolved(logger, null, operationPath, mountDirectory, markerPath,
                $"The DISM discard retry exited with code {discard.ExitCode}.");
            return;
        }

        isMounted = IsImageMounted(mountDirectory, out inventoryFailure);
        if (isMounted != false)
        {
            LogUnresolved(logger, inventoryFailure, operationPath, mountDirectory, markerPath,
                isMounted is null
                    ? "The DISM discard retry succeeded but the mounted-image inventory could not be read."
                    : "The DISM discard retry succeeded but Windows still reports the image as mounted.");
            return;
        }

        RemoveVerifiedMarker(logger, operationPath, mountDirectory, markerPath, "DiscardRetrySucceeded");
    }

    /// <summary>
    /// Reads a fresh inventory and reports whether Windows registers an image at the mount directory.
    /// Returns <see langword="null"/> when the inventory or any of its paths cannot be read, because an unknown
    /// mount state must never count as unmounted.
    /// </summary>
    private bool? IsImageMounted(string mountDirectory, out Exception? failure)
    {
        failure = null;
        try
        {
            bool isMounted = false;
            foreach (WinPeMountedImage image in _getMountedImages())
            {
                if (string.Equals(NormalizePath(image.MountPath), mountDirectory, StringComparison.OrdinalIgnoreCase))
                    isMounted = true;
            }
            return isMounted;
        }
        catch (Exception exception)
        {
            failure = exception;
            return null;
        }
    }

    /// <summary>
    /// Returns the normalized mount directory recorded in a cleanup marker, or <see langword="null"/> when the marker
    /// cannot be trusted to name one: it is not a regular file, is unreadable, empty or oversized, holds characters
    /// that no Windows path can contain, or names a path that is not strictly inside its own operation or that is
    /// reached through a reparse point. The mount directory itself is never opened.
    /// </summary>
    private static string? TryReadMarkerMountDirectory(string operationPath, string markerPath)
    {
        try
        {
            if ((File.GetAttributes(markerPath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                return null;

            string content;
            // Share every access so that reading never makes the owning session fail to delete its own marker.
            using (var stream = new FileStream(markerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > MaxCleanupMarkerBytes) return null;
                using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: false);
                content = reader.ReadToEnd();
            }

            // The path is later passed to DISM and shown to the user as a command, so reject anything that is
            // not a plain path: control characters, wildcards, quotes, redirections and stream separators.
            if (string.IsNullOrWhiteSpace(content) ||
                content.Any(character => char.IsControl(character) || character is '"' or '<' or '>' or '|' or '*' or '?') ||
                content.IndexOf(':', Math.Min(2, content.Length)) >= 0)
                return null;

            string mountDirectory = NormalizePath(content);
            if (string.Equals(mountDirectory, operationPath, StringComparison.OrdinalIgnoreCase) ||
                !IsWithin(mountDirectory, operationPath))
                return null;

            for (string? ancestor = Path.GetDirectoryName(mountDirectory);
                ancestor is not null && !string.Equals(ancestor, operationPath, StringComparison.OrdinalIgnoreCase);
                ancestor = Path.GetDirectoryName(ancestor))
            {
                if (GetExistingAttributes(ancestor)?.HasFlag(FileAttributes.ReparsePoint) == true) return null;
            }

            return mountDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Describes what the user must do when a cleanup marker keeps blocking servicing. The mount directory is named
    /// only when the marker identifies one inside its own operation; otherwise the user is told how to find it.
    /// </summary>
    private static string DescribeManualRecovery(string operationPath, string markerPath)
    {
        const string Introduction =
            "If another Foundry media operation is still running, let it finish and start the operation again. Otherwise, recover manually:";
        string? mountDirectory = TryReadMarkerMountDirectory(operationPath, markerPath);
        return mountDirectory is null
            ? string.Join(
                Environment.NewLine,
                "The cleanup marker does not name a mount directory inside this operation, so the cleanup cannot be verified automatically.",
                Introduction,
                "1. Restart Windows.",
                "2. From an elevated command prompt, run: dism /Get-MountedImageInfo",
                "3. For each mount directory listed inside the retained operation, run: dism /Unmount-Image /MountDir:<mount directory> /Discard",
                "4. Run: dism /Cleanup-Mountpoints",
                "5. Delete the cleanup marker file, then start the operation again.")
            : string.Join(
                Environment.NewLine,
                Introduction,
                "1. Restart Windows.",
                $"2. From an elevated command prompt, run: dism /Unmount-Image /MountDir:\"{mountDirectory}\" /Discard",
                "3. Run: dism /Cleanup-Mountpoints",
                "4. Start the operation again.");
    }

    private static void RemoveVerifiedMarker(
        ILogger logger,
        string operationPath,
        string mountDirectory,
        string markerPath,
        string resolution)
    {
        try
        {
            File.Delete(markerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnresolved(logger, exception, operationPath, mountDirectory, markerPath,
                "The cleanup was verified but its marker could not be removed.");
            return;
        }

        logger.Information(
            "Retained mounted-image cleanup was verified complete and its marker removed. Resolution={Resolution}, OperationPath={OperationPath}, MountDirectory={MountDirectory}, CleanupMarker={CleanupMarker}",
            resolution, operationPath, mountDirectory, markerPath);
    }

    private static void LogUnresolved(
        ILogger logger,
        Exception? exception,
        string operationPath,
        string? mountDirectory,
        string markerPath,
        string reason)
    {
        logger.Warning(exception,
            "Retained mounted-image cleanup remains unresolved; workspace deletion and further servicing stay blocked. Reason={Reason}, OperationPath={OperationPath}, MountDirectory={MountDirectory}, CleanupMarker={CleanupMarker}",
            reason, operationPath, mountDirectory, markerPath);
    }
}
