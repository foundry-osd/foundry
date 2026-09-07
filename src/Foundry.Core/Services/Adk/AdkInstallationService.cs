// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Adk;

/// <summary>Acquires authenticated ADK bundles before changing the machine installation.</summary>
public sealed class AdkInstallationService
{
    /// <summary>Gets the explicitly selected bundle version.</summary>
    public const string TargetVersion = "10.1.26100.2454";

    private static readonly HttpClient DownloadClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly IReadOnlySet<string> PublisherSubjects = new HashSet<string>(StringComparer.Ordinal)
    {
        "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
    };
    private readonly IAdkInstallationProbe probe;
    private readonly IAdkInstallerRunner runner;
    private readonly HttpClient client;
    private readonly Func<string, AdkInstallerIdentity, CancellationToken, Task> verify;

    /// <summary>Creates the fixed Microsoft bundle installation policy.</summary>
    public AdkInstallationService(IAdkInstallationProbe probe, IAdkInstallerRunner runner)
        : this(probe, runner, DownloadClient, VerifyInstallerAsync)
    {
    }

    internal AdkInstallationService(IAdkInstallationProbe probe, IAdkInstallerRunner runner, HttpClient client,
        Func<string, AdkInstallerIdentity, CancellationToken, Task> verify)
    {
        this.probe = probe;
        this.runner = runner;
        this.client = client;
        this.verify = verify;
    }

    /// <summary>Gets the independently owned native wait for application lifetime coordination.</summary>
    public Task? ActiveNativeOperation => runner.ActiveOperation;

    /// <summary>Gets whether native ownership needs explicit reconciliation before another operation.</summary>
    public bool HasUncertainOwnership { get; private set; }

    /// <summary>Gets the last native process identity and completion evidence.</summary>
    public AdkInstallerExecution? LastExecution { get; private set; }

    /// <summary>Gets retained native file protection and diagnostic metadata after uncertain completion.</summary>
    public Exception? RecoveryError { get; private set; }

    /// <summary>Performs the requested installation using a caller-owned machine operation lease.</summary>
    public async Task<AdkInstallResult> InstallAsync(string cacheDirectory, bool uninstallFirst,
        IProgress<AdkInstallationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (HasUncertainOwnership || runner.ActiveOperation is { IsCompleted: false })
            throw new InvalidOperationException("An ADK setup process still requires completion or recovery.");

        AdkInstallationStatus status = await DetectAsync().ConfigureAwait(false);
        List<int> exitCodes = [];
        bool nativeStarted = false;
        List<PreparedInstaller> stages = [];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string adk = await AcquireInstallerAsync(cacheDirectory, false, progress, cancellationToken).ConfigureAwait(false);
            string winPe = await AcquireInstallerAsync(cacheDirectory, true, progress, cancellationToken).ConfigureAwait(false);
            if (uninstallFirst)
            {
                IReadOnlyList<AdkInstalledProduct> products = probe.GetInstalledProducts();
                IReadOnlyList<AdkUninstallCommand> commands = AdkUninstallCommandSelector.SelectBundleUninstallCommands(products);
                if (commands.Count == 0)
                    throw new InvalidOperationException("Windows ADK uninstall commands were not found.");
                foreach (AdkUninstallCommand command in commands)
                {
                    bool isWinPe = Path.GetFileName(command.FileName).Equals("adkwinpesetup.exe", StringComparison.OrdinalIgnoreCase);
                    if (!isWinPe && !Path.GetFileName(command.FileName).Equals("adksetup.exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The registered command is not an ADK bundle executable.");
                    string? version = products.Where(product => product.DisplayName == command.DisplayName)
                        .Select(product => product.DisplayVersion).Distinct(StringComparer.Ordinal).SingleOrDefault();
                    if (!Version.TryParse(version, out _)) throw new InvalidDataException("The registered bundle version is unavailable.");
                    stages.Add(await PrepareAsync(command.FileName, new(isWinPe, version!),
                        ["/uninstall", "/quiet", "/norestart"], isWinPe ? "UninstallingWinPe" : "UninstallingAdk", cancellationToken).ConfigureAwait(false));
                }
            }
            stages.Add(await PrepareAsync(adk, new(false, TargetVersion), ["/quiet", "/norestart", "/features", "OptionId.DeploymentTools"], "InstallingAdk", cancellationToken).ConfigureAwait(false));
            stages.Add(await PrepareAsync(winPe, new(true, TargetVersion), ["/quiet", "/norestart"], "InstallingWinPe", cancellationToken).ConfigureAwait(false));
            for (int index = 0; index < stages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PreparedInstaller stage = stages[index];
                progress?.Report(new(45 + (index * 45 / stages.Count), stage.Stage));
                await verify(stage.Path, stage.Identity, cancellationToken).ConfigureAwait(false);
                LastExecution = await stage.Lease.RunAsync(async () =>
                {
                    nativeStarted = true;
                    AdkInstallerExecution execution = await runner.RunAsync(stage.Path, stage.Arguments, cancellationToken).ConfigureAwait(false);
                    LastExecution = execution;
                    if (execution.OwnershipUncertain || !execution.HasExited)
                    {
                        var error = new IOException("The ADK setup process has not confirmed completion. Keep its operation resources for recovery.");
                        error.Data["ProcessRootExitConfirmed"] = false;
                        error.Data["ProcessTreeTerminationConfirmed"] = false;
                        throw error;
                    }
                    return execution;
                }, cancellationToken).ConfigureAwait(false);
                if (LastExecution.ExitCode is not int exitCode)
                    throw new InvalidOperationException("ADK setup exited without an exit code.");
                exitCodes.Add(exitCode);
                if (exitCode == 1602 && LastExecution.CancellationRequested) break;
                if (exitCode is not 0 and not 3010 and not 1641)
                    throw new InvalidOperationException($"'{Path.GetFileName(stage.Path)}' exited with code {exitCode}.");
                if (exitCode == 1641 || LastExecution.CancellationRequested || cancellationToken.IsCancellationRequested)
                    break;
            }
            progress?.Report(new(95, "Verifying"));
            status = await DetectAsync().ConfigureAwait(false);
            AdkInstallResult result = AdkInstallResult.Classify(status, exitCodes);
            return !result.RebootRequired && (cancellationToken.IsCancellationRequested || LastExecution?.CancellationRequested == true)
                ? result with { Outcome = AdkInstallOutcome.Cancelled } : result;
        }
        catch (Exception error)
        {
            error.Data["AdkRebootRequired"] = exitCodes.Any(code => code is 3010 or 1641);
            bool uncertain = NativeFileLease.HasRetainedProtection(error);
            if (uncertain)
            {
                HasUncertainOwnership = true;
                RecoveryError = error;
                return new(status, AdkInstallOutcome.OwnershipUncertain, exitCodes.Any(code => code is 3010 or 1641));
            }
            if (nativeStarted)
            {
                try { status = await DetectAsync().ConfigureAwait(false); }
                catch (Exception detectionError) { error.Data["AdkFinalDetectionFailure"] = detectionError.Message; }
            }
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                bool reboot = exitCodes.Any(code => code is 3010 or 1641);
                return new(status, reboot ? AdkInstallOutcome.RebootRequired : AdkInstallOutcome.Cancelled, reboot);
            }
            error.Data["AdkFinalStatus"] = status;
            throw;
        }
        finally
        {
            foreach (PreparedInstaller stage in stages) stage.Lease.Dispose();
        }
    }

    private async Task<AdkInstallationStatus> DetectAsync()
        => await Task.Run(() => new AdkInstallationDetector(probe).Detect(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);

    private async Task<string> AcquireInstallerAsync(string cacheDirectory, bool winPe,
        IProgress<AdkInstallationProgress>? progress, CancellationToken cancellationToken)
    {
        var identity = new AdkInstallerIdentity(winPe, TargetVersion);
        string path = Path.Combine(cacheDirectory, $"{(winPe ? "adkwinpesetup" : "adksetup")}-{TargetVersion}.exe");
        if (File.Exists(path))
        {
            try
            {
                using NativeFileLease lease = NativeFileLease.OpenRead(path);
                await verify(path, identity, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(winPe ? 40 : 20, "DownloadCached"));
                return path;
            }
            catch (InvalidDataException) { }
        }
        progress?.Report(new(winPe ? 25 : 5, "Downloading"));
        // These immutable HTTPS targets are the verified resolutions of Microsoft links 2289980 and 2289981.
        var source = new Uri(winPe
            ? "https://download.microsoft.com/download/5/5/6/556e01ec-9d78-417d-b1e1-d83a2eff20bc/ADKWinPEAddons/adkwinpesetup.exe"
            : "https://download.microsoft.com/download/2/d/9/2d9c8902-3fcd-48a6-a22a-432b08bed61e/ADK/adksetup.exe");
        await ValidatedFileTransfer.DownloadAsync(client, source, path, new(null, null),
            new(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(30), 64 * 1024 * 1024),
            async (staged, token) =>
            {
                using NativeFileLease lease = NativeFileLease.OpenRead(staged);
                await verify(staged, identity, token).ConfigureAwait(false);
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(new(winPe ? 40 : 20, "Downloaded"));
        return path;
    }

    private async Task<PreparedInstaller> PrepareAsync(string path, AdkInstallerIdentity identity,
        IReadOnlyList<string> arguments, string stage, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("ADK setup must have an absolute executable path.");
        NativeFileLease lease = NativeFileLease.OpenRead(fullPath);
        try
        {
            await verify(fullPath, identity, cancellationToken).ConfigureAwait(false);
            return new(fullPath, identity, arguments, stage, lease);
        }
        catch { lease.Dispose(); throw; }
    }

    private static async Task VerifyInstallerAsync(string path, AdkInstallerIdentity identity, CancellationToken cancellationToken)
    {
        await AuthenticodeVerifier.VerifyAsync(path, PublisherSubjects, cancellationToken).ConfigureAwait(false);
        FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(path);
        string product = identity.WinPe ? "Windows Assessment and Deployment Kit Windows Preinstallation Environment Add-ons"
            : "Windows Assessment and Deployment Kit";
        string originalName = identity.WinPe ? "adkwinpesetup.exe" : "adksetup.exe";
        if (!string.Equals(metadata.ProductName, product, StringComparison.Ordinal) ||
            !string.Equals(metadata.OriginalFilename, originalName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(metadata.ProductVersion, identity.Version, StringComparison.Ordinal) ||
            !string.Equals(metadata.FileVersion, identity.Version, StringComparison.Ordinal))
            throw new InvalidDataException("The signed executable does not match the expected ADK bundle product and version.");
    }

    private sealed record PreparedInstaller(string Path, AdkInstallerIdentity Identity, IReadOnlyList<string> Arguments, string Stage, NativeFileLease Lease);
}

/// <summary>Reports a localized-adapter stage and its percentage.</summary>
public sealed record AdkInstallationProgress(int Percent, string Stage);

/// <summary>Defines the fixed product family and expected bundle version for byte validation.</summary>
internal sealed record AdkInstallerIdentity(bool WinPe, string Version);
