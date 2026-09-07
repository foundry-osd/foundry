// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.ExceptionServices;
using System.IO;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Owns boot configuration and the lifetime of recovery image servicing.</summary>
public sealed class BootRecoveryService : IBootRecoveryService
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(2);
    private readonly WindowsDeploymentCommandRunner commands;
    private readonly IWindowsImagingService imaging;
    private readonly Func<string, bool> fileExists;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    /// <inheritdoc />
    public RecoveryResourceDiagnostic? RecoveryDiagnostic { get; private set; }

    /// <summary>Uses the shared process policy and imaging owner without retaining a mutable target layout.</summary>
    public BootRecoveryService(IProcessRunner processRunner, ILogger<BootRecoveryService> logger, IWindowsImagingService imaging)
        : this(processRunner, logger, imaging, File.Exists) { }

    internal BootRecoveryService(IProcessRunner processRunner, ILogger<BootRecoveryService> logger, IWindowsImagingService imaging,
        Func<string, bool> fileExists)
    {
        commands = new(processRunner, logger);
        this.imaging = imaging;
        this.fileExists = fileExists;
    }

    /// <inheritdoc />
    public async Task ConfigureBootAsync(DeploymentTargetLayout retainedLayout, string windowsPartitionRoot,
        string systemPartitionRoot, int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfRecoveryRequired();
            ArgumentNullException.ThrowIfNull(retainedLayout);
            if (retainedLayout.WindowsPartitionRoot != windowsPartitionRoot || retainedLayout.SystemPartitionRoot != systemPartitionRoot ||
                retainedLayout.DiskIdentity is null || retainedLayout.WindowsPartition is null || retainedLayout.SystemPartition is null)
                throw new InvalidOperationException("The retained boot partition identity is unavailable or changed.");
            await RunStorageScriptAsync(TargetDiskPreparationScript.Validate(retainedLayout.DiskIdentity, retainedLayout.WindowsPartition), workingDirectory, cancellationToken).ConfigureAwait(false);
            await RunStorageScriptAsync(TargetDiskPreparationScript.Validate(retainedLayout.DiskIdentity, retainedLayout.SystemPartition, verifyLetter: true), workingDirectory, cancellationToken).ConfigureAwait(false);
            await RunBcdBootAsync(windowsPartitionRoot, $"{retainedLayout.SystemPartition.DriveLetter}:", operatingSystemBuildMajor, workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally { operationGate.Release(); }
    }

    internal async Task RunBcdBootAsync(string windowsPartitionRoot, string systemPartitionRoot,
        int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default)
    {
        ThrowIfRecoveryRequired();
        string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
        string executable = Path.Combine(windowsPath, "System32", "bcdboot.exe");
        if (!fileExists(executable)) throw new FileNotFoundException("The applied Windows image does not contain bcdboot.exe.", executable);
        List<string> arguments = [windowsPath, "/s", systemPartitionRoot.TrimEnd('\\', '/'), "/f", "UEFI", "/c"];
        if (operatingSystemBuildMajor >= 26200) arguments.Add("/bootex");
        arguments.Add("/v");
        await commands.RunRequiredProcessAsync(executable, arguments, workingDirectory, "BCDBoot configuration failed", cancellationToken, CleanupTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SealRecoveryPartitionAsync(DeploymentTargetLayout retainedLayout, string recoveryPartitionRoot,
        char recoveryPartitionLetter, string workingDirectory, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfRecoveryRequired();
            ArgumentNullException.ThrowIfNull(retainedLayout);
            if (retainedLayout.RecoveryPartitionRoot != recoveryPartitionRoot || retainedLayout.RecoveryPartitionLetter != recoveryPartitionLetter ||
                retainedLayout.DiskIdentity is null || retainedLayout.RecoveryPartition is null)
                throw new InvalidOperationException("The retained recovery partition identity is unavailable or changed.");
            await RunStorageScriptAsync(TargetDiskPreparationScript.Validate(retainedLayout.DiskIdentity, retainedLayout.RecoveryPartition, removeLetter: true), workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally { operationGate.Release(); }
    }

    /// <inheritdoc />
    public async Task ConfigureRecoveryEnvironmentAsync(string windowsPartitionRoot, string recoveryPartitionRoot,
        string workingDirectory, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfRecoveryRequired();
            ArgumentException.ThrowIfNullOrWhiteSpace(windowsPartitionRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(recoveryPartitionRoot);
            string windowsPath = Path.Combine(windowsPartitionRoot, "Windows");
            string source = Path.Combine(windowsPath, "System32", "Recovery", "winre.wim");
            if (!File.Exists(source)) throw new FileNotFoundException("The offline Windows image does not contain winre.wim.", source);
            string executable = Path.Combine(Environment.SystemDirectory, "winrecfg.exe");
            if (!fileExists(executable)) throw new FileNotFoundException("Required WinPE executable 'winrecfg.exe' was not found. Add the WinPE-WinReCfg optional component to the WinPE image.", executable);
            string target = GetRecoveryImagePath(recoveryPartitionRoot);
            Directory.CreateDirectory(workingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
            await commands.RunRequiredProcessAsync(executable, ["/setreimage", "/path", Path.GetDirectoryName(target)!, "/target", windowsPath],
                workingDirectory, "Failed to set the Windows RE image location", cancellationToken).ConfigureAwait(false);
        }
        finally { operationGate.Release(); }
    }

    /// <inheritdoc />
    public async Task ApplyRecoveryDriversAsync(string recoveryPartitionRoot, string driverRoot, string scratchDirectory,
        string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? mountProgress = null,
        IProgress<double>? applyProgress = null, IProgress<double>? unmountProgress = null,
        Action? onMountStarted = null, Action? onApplyStarted = null, Action? onUnmountStarted = null)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfRecoveryRequired();
            ArgumentException.ThrowIfNullOrWhiteSpace(recoveryPartitionRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(driverRoot);
            string image = Path.GetFullPath(GetRecoveryImagePath(recoveryPartitionRoot));
            if (!File.Exists(image)) throw new FileNotFoundException("The recovery partition does not contain winre.wim.", image);
            string mount = Path.Combine(Path.GetFullPath(workingDirectory), "Mount-WindowsRE-" + Guid.NewGuid().ToString("N"));
            RejectReparsePoints(mount);
            RejectReparsePoints(image);
            Directory.CreateDirectory(scratchDirectory);
            Directory.CreateDirectory(mount);
            Exception? primary = null;
            bool servicingSucceeded = false;
            bool nativeActive = false;
            bool terminationConfirmed = true;
            try
            {
                if (await InspectAsync(image, mount, workingDirectory, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("The new recovery mount path is already registered.");
                onMountStarted?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                nativeActive = true;
                await RunDismAsync(["/Mount-Image", $"/ImageFile:{image}", "/Index:1", $"/MountDir:{mount}", $"/ScratchDir:{scratchDirectory}"],
                    workingDirectory, "Failed to mount the Windows RE image", cancellationToken, mountProgress).ConfigureAwait(false);
                nativeActive = false;
                onApplyStarted?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                nativeActive = true;
                await imaging.ApplyOfflineDriversAsync(mount, driverRoot, scratchDirectory, workingDirectory, cancellationToken, applyProgress).ConfigureAwait(false);
                nativeActive = false;
                servicingSucceeded = true;
            }
            catch (Exception error)
            {
                primary = error;
                terminationConfirmed = !nativeActive || IsTerminationConfirmed(error);
            }

            using var cleanup = new CancellationTokenSource(CleanupTimeout);
            Exception? cleanupFailure = null;
            bool absent = false;
            try
            {
                bool mounted = await InspectAsync(image, mount, workingDirectory, cleanup.Token).ConfigureAwait(false);
                if (!terminationConfirmed) throw new InvalidOperationException("Native termination is unconfirmed; retain recovery resources.");
                if (mounted)
                {
                    try
                    {
                        onUnmountStarted?.Invoke();
                        await RunDismAsync(["/Unmount-Image", $"/MountDir:{mount}", servicingSucceeded ? "/Commit" : "/Discard"],
                            workingDirectory, "Failed to unmount the Windows RE image", cleanup.Token, unmountProgress, CleanupTimeout).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        cleanupFailure = error;
                        terminationConfirmed = IsTerminationConfirmed(error);
                    }
                    absent = !await InspectAsync(image, mount, workingDirectory, cleanup.Token).ConfigureAwait(false);
                }
                else absent = true;
                if (!absent || !terminationConfirmed) throw new InvalidOperationException("Recovery mount absence or native termination could not be confirmed.");
                RejectReparsePoints(mount);
                Directory.Delete(mount, recursive: true);
            }
            catch (Exception error) { cleanupFailure ??= error; }

            if (!absent || !terminationConfirmed || Directory.Exists(mount))
            {
                RecoveryDiagnostic = new(RecoveryResourceState.RecoveryRequired, "WinRE mount", mount, image,
                    "Recovery servicing resources require explicit reconciliation before further mutation.");
                primary ??= cleanupFailure ?? new InvalidOperationException(RecoveryDiagnostic.Reason);
                primary.Data["FoundryRecoveryDiagnostic"] = RecoveryDiagnostic;
            }
            if (cleanupFailure is not null)
            {
                if (primary is null) primary = cleanupFailure;
                else if (!ReferenceEquals(primary, cleanupFailure)) primary.Data["FoundryCleanupFailure"] = cleanupFailure;
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        }
        finally { operationGate.Release(); }
    }

    private async Task<bool> InspectAsync(string image, string mount, string workingDirectory, CancellationToken token)
    {
        ProcessExecutionResult result = await commands.RunRequiredProcessAsync("dism.exe", ["/Get-MountedImageInfo", "/English"], workingDirectory,
            "Failed to inspect recovery mount ownership", token, CleanupTimeout).ConfigureAwait(false);
        result.EnsureCompleteOutput();
        return WinPeMountRecovery.ParseOwnedMount(result.StandardOutput, image, mount);
    }

    private async Task RunDismAsync(string[] arguments, string workingDirectory, string failure, CancellationToken token,
        IProgress<double>? progress, TimeSpan? timeout = null)
    {
        DismProgressReporter? reporter = progress is null ? null : new(progress);
        await commands.RunRequiredProcessAsync("dism.exe", arguments, workingDirectory, failure, token,
            reporter is null ? null : reporter.HandleOutput, reporter is null ? null : reporter.HandleOutput, timeout).ConfigureAwait(false);
        if (reporter?.HasReportedProgress == true) progress!.Report(100d);
    }

    private Task<ProcessExecutionResult> RunStorageScriptAsync(string script, string work, CancellationToken token)
        => commands.RunRequiredProcessAsync("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }
            .Concat(PowerShellCommand.CreateEncodedArguments(script)), work, "The confirmed disk or partition operation failed", token, CleanupTimeout);

    private void ThrowIfRecoveryRequired()
    {
        if (RecoveryDiagnostic is null) return;
        var error = new InvalidOperationException("Recovery resources require reconciliation before further boot or recovery operations.");
        error.Data["FoundryRecoveryDiagnostic"] = RecoveryDiagnostic;
        throw error;
    }

    private static string GetRecoveryImagePath(string root) => Path.Combine(root, "Recovery", "WindowsRE", "winre.wim");

    private static bool IsTerminationConfirmed(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is OperationCanceledException or TimeoutException || current.Data.Contains("ProcessRootExitConfirmed") ||
                current.Data.Contains("ProcessTreeTerminationConfirmed") || current.Data.Contains("ProcessOutputDrainConfirmed"))
                return current.Data["ProcessRootExitConfirmed"] is true && current.Data["ProcessTreeTerminationConfirmed"] is true;
        return true;
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery paths cannot traverse reparse points.");
    }
}
