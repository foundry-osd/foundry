// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Storage;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Serilog;

namespace Foundry.Services.Adk;

/// <summary>
/// Coordinates Windows ADK detection and elevated installer execution for the main application.
/// </summary>
internal sealed class AdkService(
    IAdkInstallationProbe installationProbe,
    IOperationProgressService operationProgressService,
    IApplicationLocalizationService localizationService,
    ILogger logger) : IAdkService
{
    private const string TargetAdkVersion = "10.1.26100.2454";
    private const string AdkSetupFileName = $"adksetup-{TargetAdkVersion}.exe";
    private const string WinPeSetupFileName = $"adkwinpesetup-{TargetAdkVersion}.exe";
    private const string AdkSetupUrl = "https://go.microsoft.com/fwlink/?linkid=2289980";
    private const string WinPeSetupUrl = "https://go.microsoft.com/fwlink/?linkid=2289981";
    private const string AdkInstallArguments = "/quiet /norestart /features OptionId.DeploymentTools";
    private const string WinPeInstallArguments = "/quiet /norestart";
    private static readonly HttpClient HttpClient = new();

    private readonly ILogger logger = logger.ForContext<AdkService>();
    private readonly SemaphoreSlim operationLock = new(1, 1);

    /// <inheritdoc />
    public event EventHandler<AdkStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public AdkInstallationStatus CurrentStatus { get; private set; } = new(
        false,
        false,
        false,
        null,
        AdkVersionRelation.Unknown,
        null,
        "Windows ADK 24H2 / 10.1.26100.2454");

    /// <inheritdoc />
    public Task<AdkInstallationStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AdkInstallationStatus status = new AdkInstallationDetector(installationProbe).Detect();
        ApplyStatus(status);

        logger.Information(
            "ADK status refreshed. IsInstalled={IsInstalled}, IsCompatible={IsCompatible}, IsWinPeAddonInstalled={IsWinPeAddonInstalled}, InstalledVersion={InstalledVersion}, IsWinPeAddonCompatible={IsWinPeAddonCompatible}, IsX64Available={IsX64Available}, IsArm64Available={IsArm64Available}",
            status.IsInstalled,
            status.IsCompatible,
            status.IsWinPeAddonInstalled,
            status.InstalledVersion,
            status.IsWinPeAddonCompatible,
            status.IsX64Available,
            status.IsArm64Available);

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<AdkInstallationStatus> InstallAsync(CancellationToken cancellationToken = default)
    {
        return RunInstallOperationAsync(OperationKind.AdkInstall, uninstallFirst: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdkInstallationStatus> UpgradeAsync(CancellationToken cancellationToken = default)
    {
        return RunInstallOperationAsync(OperationKind.AdkUpgrade, uninstallFirst: true, cancellationToken);
    }

    private async Task<AdkInstallationStatus> RunInstallOperationAsync(
        OperationKind operationKind,
        bool uninstallFirst,
        CancellationToken cancellationToken)
    {
        // ADK setup changes machine-level state and may show UAC, so only one install or upgrade can run at a time.
        await operationLock.WaitAsync(cancellationToken);
        string terminalStatus = string.Empty;

        try
        {
            operationProgressService.Start(operationKind, GetOperationStartText(operationKind));
            Directory.CreateDirectory(Constants.InstallerCacheDirectoryPath);

            await using CachedArtifactLease adkSetup = await DownloadInstallerAsync(AdkSetupUrl, AdkSetupFileName, uninstallFirst ? 35 : 20, cancellationToken);
            await using CachedArtifactLease winPeSetup = await DownloadInstallerAsync(WinPeSetupUrl, WinPeSetupFileName, uninstallFirst ? 45 : 40, cancellationToken);
            if (uninstallFirst)
            {
                await UninstallExistingBundlesAsync(cancellationToken);
            }

            operationProgressService.Report(uninstallFirst ? 70 : 55, localizationService.GetString("Adk.Operation.InstallingAdk"));
            await RunElevatedProcessAsync(adkSetup.Path, AdkInstallArguments, cancellationToken);

            operationProgressService.Report(uninstallFirst ? 88 : 80, localizationService.GetString("Adk.Operation.InstallingWinPe"));
            await RunElevatedProcessAsync(winPeSetup.Path, WinPeInstallArguments, cancellationToken);

            operationProgressService.Report(95, localizationService.GetString("Adk.Operation.Verifying"));
            AdkInstallationStatus status = await RefreshStatusAsync(cancellationToken);
            terminalStatus = localizationService.GetString(status.CanCreateMedia ? "Adk.Operation.Completed" : "Adk.Operation.NeedsAttention");
            operationProgressService.Complete(terminalStatus);
            logger.Information(
                "ADK operation completed. OperationKind={OperationKind}, IsCompatible={IsCompatible}, InstalledVersion={InstalledVersion}",
                operationKind,
                status.IsCompatible,
                status.InstalledVersion);

            return status;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "ADK operation failed. OperationKind={OperationKind}", operationKind);
            terminalStatus = localizationService.GetString("Adk.Operation.Failed");
            operationProgressService.Report(100, terminalStatus);
            throw;
        }
        finally
        {
            operationProgressService.Reset(terminalStatus);
            operationLock.Release();
        }
    }

    private async Task<CachedArtifactLease> DownloadInstallerAsync(
        string url,
        string fileName,
        int completedProgress,
        CancellationToken cancellationToken)
    {
        // These source links are selected together with TargetAdkVersion. Without a publisher
        // digest, receipts prove completed-transfer consistency only, not publisher authenticity.
        // Legacy flat EXEs have no such receipt and are retained rather than silently adopted.
        var request = new CachedArtifactRequest(AuthoringArtifactKind.Installer,
            $"ADK/{TargetAdkVersion}/{fileName}/{url}", fileName, null, null, AllowCompletedTransferReuse: true);
        CachedArtifactLease lease = await new AuthoringArtifactCache(Constants.InstallerCacheDirectoryPath).AcquireAsync(request, async (path, token) =>
        {
            operationProgressService.Report(Math.Max(0, completedProgress - 10), localizationService.GetString("Adk.Operation.Downloading"));
            logger.Information("Downloading versioned ADK installer. Version={Version}, FileName={FileName}", TargetAdkVersion, fileName);
            using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using Stream input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
            if (response.Content.Headers.ContentLength is long length && output.Length != length)
                throw new InvalidDataException("ADK installer transfer is incomplete.");
        }, cancellationToken);
        operationProgressService.Report(completedProgress, localizationService.GetString(lease.CacheHit ? "Adk.Operation.DownloadCached" : "Adk.Operation.Downloaded"));
        logger.Information("ADK installer acquired. Version={Version}, CacheHit={CacheHit}, IntegrityPolicy={IntegrityPolicy}",
            TargetAdkVersion, lease.CacheHit, "CompletedVersionedTransfer");
        return lease;
    }
    private async Task UninstallExistingBundlesAsync(CancellationToken cancellationToken)
    {
        // Upgrade uses the registered uninstall commands instead of assuming a fixed ADK install location.
        IReadOnlyList<AdkUninstallCommand> uninstallCommands = AdkUninstallCommandSelector.SelectBundleUninstallCommands(
            installationProbe.GetInstalledProducts());

        if (uninstallCommands.Count == 0)
        {
            throw new InvalidOperationException("Windows ADK uninstall commands were not found in the Windows uninstall registry.");
        }

        foreach (AdkUninstallCommand command in uninstallCommands)
        {
            operationProgressService.Report(
                command.FileName.EndsWith("adkwinpesetup.exe", StringComparison.OrdinalIgnoreCase) ? 45 : 55,
                command.FileName.EndsWith("adkwinpesetup.exe", StringComparison.OrdinalIgnoreCase)
                    ? localizationService.GetString("Adk.Operation.UninstallingWinPe")
                    : localizationService.GetString("Adk.Operation.UninstallingAdk"));

            logger.Information(
                "Uninstalling existing ADK bundle. DisplayName={DisplayName}, FileName={FileName}",
                command.DisplayName,
                Path.GetFileName(command.FileName));
            await RunElevatedProcessAsync(command.FileName, command.Arguments, cancellationToken);
        }
    }

    private static async Task RunElevatedProcessAsync(string setupPath, string arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(setupPath))
        {
            throw new FileNotFoundException("ADK setup executable was not found.", setupPath);
        }

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        }) ?? throw new InvalidOperationException($"Unable to start '{setupPath}'.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode is not 0 and not 3010)
        {
            throw new InvalidOperationException($"'{Path.GetFileName(setupPath)}' exited with code {process.ExitCode}.");
        }
    }

    private string GetOperationStartText(OperationKind operationKind)
    {
        return operationKind == OperationKind.AdkUpgrade
            ? localizationService.GetString("Adk.Operation.UpgradeStarted")
            : localizationService.GetString("Adk.Operation.InstallStarted");
    }

    private void ApplyStatus(AdkInstallationStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, new(status));
    }
}
