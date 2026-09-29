// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
        string operationId = Guid.NewGuid().ToString("N");
        string stage = "download_adk";
        ILogger operationLogger = logger.ForContext("OperationId", operationId)
            .ForContext("OperationKind", operationKind);

        try
        {
            operationProgressService.Start(operationKind, GetOperationStartText(operationKind));
            operationLogger.Information("ADK operation started. OperationKind={OperationKind}", operationKind);
            Directory.CreateDirectory(Constants.InstallerCacheDirectoryPath);

            await using CachedArtifactLease adkSetup = await DownloadInstallerAsync(AdkSetupUrl, AdkSetupFileName, uninstallFirst ? 35 : 20, cancellationToken);
            stage = "download_winpe";
            await using CachedArtifactLease winPeSetup = await DownloadInstallerAsync(WinPeSetupUrl, WinPeSetupFileName, uninstallFirst ? 45 : 40, cancellationToken);
            if (uninstallFirst)
            {
                stage = "uninstall";
                await UninstallExistingBundlesAsync(operationId, operationLogger, cancellationToken);
            }

            stage = "install_adk";
            operationProgressService.Report(uninstallFirst ? 70 : 55, localizationService.GetString("Adk.Operation.InstallingAdk"));
            await RunSetupAsync(adkSetup.Path, AdkInstallArguments, operationId, stage, operationLogger, cancellationToken);

            stage = "install_winpe";
            operationProgressService.Report(uninstallFirst ? 88 : 80, localizationService.GetString("Adk.Operation.InstallingWinPe"));
            await RunSetupAsync(winPeSetup.Path, WinPeInstallArguments, operationId, stage, operationLogger, cancellationToken);

            stage = "verify";
            operationProgressService.Report(95, localizationService.GetString("Adk.Operation.Verifying"));
            AdkInstallationStatus status = await RefreshStatusAsync(cancellationToken);
            terminalStatus = localizationService.GetString(status.CanCreateMedia ? "Adk.Operation.Completed" : "Adk.Operation.NeedsAttention");
            operationProgressService.Complete(terminalStatus);
            operationLogger.ForContext("Outcome", status.CanCreateMedia ? "succeeded" : "needs_attention").Information(
                "ADK operation completed. OperationKind={OperationKind}, IsCompatible={IsCompatible}, InstalledVersion={InstalledVersion}",
                operationKind,
                status.IsCompatible,
                status.InstalledVersion);

            return status;
        }
        catch (AdkSetupException ex) when (ex.Reason == "elevation_cancelled")
        {
            CreateSetupFailureLogger(operationLogger, ex, stage)
                .ForContext("Cancelled", true).ForContext("Outcome", "cancelled")
                .Warning("ADK operation canceled. FailureReason={FailureReason}", ex.Reason);
            terminalStatus = localizationService.GetString("Adk.Operation.Cancelled");
            operationProgressService.Report(100, terminalStatus);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operationLogger.ForContext("FailedOperationName", stage)
                .ForContext("Cancelled", true).ForContext("Outcome", "cancelled")
                .Warning("ADK operation canceled. FailureReason={FailureReason}", "operation_cancelled");
            terminalStatus = localizationService.GetString("Adk.Operation.Cancelled");
            operationProgressService.Report(100, terminalStatus);
            throw;
        }
        catch (Exception ex)
        {
            AdkSetupException? setupException = ex as AdkSetupException;
            ILogger failureLogger = setupException is not null
                ? CreateSetupFailureLogger(operationLogger, setupException, stage)
                : operationLogger.ForContext("FailedOperationName", stage).ForContext("FailureKind", "adk");
            failureLogger.ForContext("Outcome", "failed").Error(ex,
                "ADK operation failed. OperationKind={OperationKind}, FailureReason={FailureReason}, ExitCode={ExitCode}, NativeErrorCode={NativeErrorCode}",
                operationKind, setupException?.Reason ?? "operation_failed",
                setupException?.ExitCode, setupException?.NativeErrorCode);
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
    private async Task UninstallExistingBundlesAsync(string operationId, ILogger operationLogger, CancellationToken cancellationToken)
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

            operationLogger.Information(
                "Uninstalling existing ADK bundle. DisplayName={DisplayName}, FileName={FileName}",
                command.DisplayName,
                Path.GetFileName(command.FileName));
            string stage = GetUninstallStage(command.FileName);
            await RunSetupAsync(command.FileName, command.Arguments, operationId, stage, operationLogger, cancellationToken);
        }
    }

    private static async Task RunSetupAsync(
        string setupPath, string arguments, string operationId, string stage,
        ILogger operationLogger, CancellationToken cancellationToken)
    {
        string logId = $"{operationId}-{stage}";
        string logPath = Path.Combine(Constants.LogDirectoryPath, "Adk", $"{logId}.log");
        ILogger setupLogger = operationLogger.ForContext("Stage", stage)
            .ForContext("InstallerName", Path.GetFileName(setupPath))
            .ForContext("SetupLogId", logId).ForContext("SetupLogPath", logPath);
        if (stage is "install_adk" or "install_winpe")
            setupLogger = setupLogger.ForContext("InstallerVersion", TargetAdkVersion);

        setupLogger.Information("ADK setup starting. Stage={Stage}, SetupLogId={SetupLogId}", stage, logId);
        int exitCode = await new AdkSetupRunner().RunAsync(setupPath, arguments, logPath, cancellationToken);
        setupLogger.ForContext("ExitCode", exitCode).ForContext("ExitCodeHex", $"0x{exitCode:X8}")
            .Information("ADK setup completed. Stage={Stage}, ExitCode={ExitCode}", stage, exitCode);
    }

    /// <summary>Preserves native failure details without putting machine paths in Error Tracking fields.</summary>
    private static ILogger CreateSetupFailureLogger(ILogger operationLogger, AdkSetupException exception, string stage)
    {
        if (stage == "uninstall") stage = GetUninstallStage(exception.SetupPath);
        ILogger failureLogger = operationLogger.ForContext("FailureKind", "adk_setup")
            .ForContext("FailedOperationName", stage)
            .ForContext("InstallerName", Path.GetFileName(exception.SetupPath))
            .ForContext("SetupLogId", Path.GetFileNameWithoutExtension(exception.LogPath))
            .ForContext("SetupLogPath", exception.LogPath);
        if (stage is "install_adk" or "install_winpe")
            failureLogger = failureLogger.ForContext("InstallerVersion", TargetAdkVersion);
        if (exception.ExitCode is int exitCode)
            failureLogger = failureLogger.ForContext("ExitCode", exitCode).ForContext("ExitCodeHex", $"0x{exitCode:X8}");
        if (exception.NativeErrorCode is int nativeErrorCode)
            failureLogger = failureLogger.ForContext("NativeErrorCode", nativeErrorCode);
        return failureLogger;
    }

    private static string GetUninstallStage(string setupPath) =>
        setupPath.EndsWith("adkwinpesetup.exe", StringComparison.OrdinalIgnoreCase)
            ? "uninstall_winpe" : "uninstall_adk";

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
