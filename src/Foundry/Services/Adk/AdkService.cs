using System.Diagnostics;
using Foundry.Core.Services.Adk;
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
        null,
        "Windows ADK 10.1.26100.2454+ with the latest ADK servicing patch");

    /// <inheritdoc />
    public Task<AdkInstallationStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AdkInstallationStatus status = new AdkInstallationDetector(installationProbe).Detect();
        ApplyStatus(status);

        logger.Information(
            "ADK status refreshed. IsInstalled={IsInstalled}, IsCompatible={IsCompatible}, IsWinPeAddonInstalled={IsWinPeAddonInstalled}, InstalledVersion={InstalledVersion}",
            status.IsInstalled,
            status.IsCompatible,
            status.IsWinPeAddonInstalled,
            status.InstalledVersion);

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

            string adkSetupPath = Path.Combine(Constants.InstallerCacheDirectoryPath, AdkSetupFileName);
            string winPeSetupPath = Path.Combine(Constants.InstallerCacheDirectoryPath, WinPeSetupFileName);

            await DownloadInstallerAsync(AdkSetupUrl, adkSetupPath, uninstallFirst ? 35 : 20, cancellationToken);
            await DownloadInstallerAsync(WinPeSetupUrl, winPeSetupPath, uninstallFirst ? 45 : 40, cancellationToken);

            if (uninstallFirst)
            {
                await UninstallExistingBundlesAsync(cancellationToken);
            }

            operationProgressService.Report(uninstallFirst ? 70 : 55, localizationService.GetString("Adk.Operation.InstallingAdk"));
            await RunElevatedProcessAsync(adkSetupPath, AdkInstallArguments, cancellationToken);

            operationProgressService.Report(uninstallFirst ? 88 : 80, localizationService.GetString("Adk.Operation.InstallingWinPe"));
            await RunElevatedProcessAsync(winPeSetupPath, WinPeInstallArguments, cancellationToken);

            operationProgressService.Report(95, localizationService.GetString("Adk.Operation.Verifying"));
            AdkInstallationStatus status = await RefreshStatusAsync(cancellationToken);
            terminalStatus = localizationService.GetString("Adk.Operation.Completed");
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

    private async Task DownloadInstallerAsync(
        string url,
        string outputPath,
        int completedProgress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
        {
            FileInfo cachedInstaller = new(outputPath);
            if (cachedInstaller.Length == 0)
            {
                File.Delete(outputPath);
            }
            else
            {
                operationProgressService.Report(completedProgress, localizationService.GetString("Adk.Operation.DownloadCached"));
                logger.Debug("ADK installer cache hit. Url={Url}, OutputPath={OutputPath}", url, outputPath);
                return;
            }
        }

        operationProgressService.Report(Math.Max(0, completedProgress - 10), localizationService.GetString("Adk.Operation.Downloading"));
        logger.Information("Downloading ADK installer. Url={Url}, OutputPath={OutputPath}", url, outputPath);
        string temporaryOutputPath = $"{outputPath}.download";
        if (File.Exists(temporaryOutputPath))
        {
            File.Delete(temporaryOutputPath);
        }

        using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (FileStream output = File.Create(temporaryOutputPath))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        File.Move(temporaryOutputPath, outputPath, overwrite: true);

        operationProgressService.Report(completedProgress, localizationService.GetString("Adk.Operation.Downloaded"));
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
