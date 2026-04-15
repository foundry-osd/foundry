using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.WinPe;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Foundry.Services.Adk;

/// <summary>
/// Implementation of the Windows ADK management service.
/// </summary>
public sealed class AdkService : IAdkService
{
    private const string Adk24H2Version = "10.1.26100.1"; // Windows 11 24H2 ADK version (checked as 10.1.26100.*)
    private const string AdkRegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";
    private const string AdkRegistryKey = "KitsRoot10";
    private const string AdkDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2289980"; // Windows ADK for Windows 11, version 24H2
    private const string WinPeAddonDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2289981"; // WinPE Add-on for Windows 11, version 24H2
    private const string AdkBasePath = "Assessment and Deployment Kit";
    private const string DeploymentToolsPath = "Deployment Tools";
    private const string WinPeEnvironmentPath = "Windows Preinstallation Environment";

    private const string AdkInstallArguments = "/quiet /norestart /features OptionId.DeploymentTools";
    private const string WinPeInstallArguments = "/quiet /norestart";
    private const string AdkUninstallArguments = "/uninstall /quiet /norestart";
    private const string WinPeUninstallArguments = "/uninstall /quiet /norestart";
    
    // Keep these windows configurable in code so status checks are bounded but tolerant.
    private const int InstallStatePollDelayMs = 1000;
    private static readonly TimeSpan InstallStateWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeferredInstallStateWaitTimeout = TimeSpan.FromMinutes(15);

    // Progress ranges are operation-specific to keep progress monotonic within each workflow.
    private const int DownloadDefaultStart = 0;
    private const int DownloadDefaultEnd = 100;

    private const int InstallDownloadStart = 0;
    private const int InstallDownloadEnd = 40;
    private const int InstallAdkStart = 40;
    private const int InstallAdkEnd = 75;
    private const int InstallWinPeStart = 75;
    private const int InstallWinPeEnd = 95;
    private const int InstallVerifyStart = 95;
    private const int InstallVerifyEnd = 100;

    private const int UninstallDownloadStart = 0;
    private const int UninstallDownloadEnd = 40;
    private const int UninstallWinPeStart = 40;
    private const int UninstallWinPeEnd = 70;
    private const int UninstallAdkStart = 70;
    private const int UninstallAdkEnd = 90;
    private const int UninstallVerifyStart = 90;
    private const int UninstallVerifyEnd = 100;

    private const int UpgradeUninstallPrepStart = 0;
    private const int UpgradeUninstallPrepEnd = 10;
    private const int UpgradeUninstallWinPeStart = 10;
    private const int UpgradeUninstallWinPeEnd = 18;
    private const int UpgradeUninstallAdkStart = 18;
    private const int UpgradeUninstallAdkEnd = 25;
    private const int UpgradeDownloadStart = 25;
    private const int UpgradeDownloadEnd = 60;
    private const int UpgradeInstallAdkStart = 60;
    private const int UpgradeInstallAdkEnd = 80;
    private const int UpgradeInstallWinPeStart = 80;
    private const int UpgradeInstallWinPeEnd = 95;
    private const int UpgradeVerifyStart = 95;
    private const int UpgradeVerifyEnd = 100;

    private bool _isAdkInstalled;
    private bool _isAdkCompatible;
    private string? _installedVersion;
    private readonly ILocalizationService _localizationService;
    private readonly IOperationProgressService _operationProgressService;
    private readonly ILogger<AdkService> _logger;
    private string? _downloadedAdkInstallerPath;
    private string? _downloadedWinPeInstallerPath;

    public bool IsAdkInstalled => _isAdkInstalled;
    public bool IsAdkCompatible => _isAdkCompatible;
    public string? InstalledVersion => _installedVersion;
    public bool IsOperationInProgress =>
        _operationProgressService.IsOperationInProgress &&
        IsAdkOperation(_operationProgressService.CurrentOperation);
    public bool IsAnyOperationInProgress => _operationProgressService.IsOperationInProgress;
    public int OperationProgress => _operationProgressService.Progress;
    public string? OperationStatus => _operationProgressService.Status;

    public event EventHandler? AdkStatusChanged;
    public event EventHandler? OperationProgressChanged;

    public AdkService(
        ILocalizationService localizationService,
        IOperationProgressService operationProgressService,
        ILogger<AdkService> logger)
    {
        _localizationService = localizationService;
        _operationProgressService = operationProgressService;
        _logger = logger;
        _operationProgressService.ProgressChanged += OnGlobalProgressChanged;
        RefreshStatus();
    }

    /// <summary>
    /// Recomputes ADK installation state and notifies subscribers.
    /// </summary>
    public void RefreshStatus()
    {
        _isAdkInstalled = CheckAdkInstalled();
        _installedVersion = GetInstalledVersion();
        _isAdkCompatible = CheckAdkCompatible();
        _logger.LogDebug(
            "ADK status refreshed. Installed={IsAdkInstalled}, Compatible={IsAdkCompatible}, Version={InstalledVersion}",
            _isAdkInstalled,
            _isAdkCompatible,
            _installedVersion ?? "<none>");
        AdkStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DownloadAdkAsync()
    {
        _logger.LogInformation("ADK download requested.");
        if (!_operationProgressService.TryStart(
                OperationKind.AdkDownload,
                L("Adk.StatusStartDownload"),
                DownloadDefaultStart))
        {
            _logger.LogWarning("ADK download skipped because another operation is already in progress.");
            return;
        }

        try
        {
            await EnsureInstallersDownloadedAsync(DownloadDefaultStart, DownloadDefaultEnd, forceDownload: true);

            _operationProgressService.Complete(L("Adk.StatusDoneDownload"));
            _logger.LogInformation("ADK download completed successfully.");
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(Lf("Adk.ErrorDownload", ex.Message));
            _logger.LogError(ex, "ADK download failed.");
            throw;
        }
    }

    public async Task InstallAdkAsync()
    {
        _logger.LogInformation("ADK install requested.");
        if (!_operationProgressService.TryStart(
                OperationKind.AdkInstall,
                L("Adk.StatusStartInstall"),
                0))
        {
            _logger.LogWarning("ADK install skipped because another operation is already in progress.");
            return;
        }

        try
        {
            await EnsureInstallersDownloadedAsync(InstallDownloadStart, InstallDownloadEnd, forceDownload: false);
            await InstallAdkInternalAsync(InstallAdkStart, InstallAdkEnd, InstallWinPeStart, InstallWinPeEnd);
            var synchronizationContext = SynchronizationContext.Current;
            var installStateConfirmed = await WaitForInstallStateAsync(expectedInstalled: true, InstallVerifyStart, InstallVerifyEnd);
            if (!installStateConfirmed)
            {
                StartDeferredStatusRefresh(expectedInstalled: true, synchronizationContext);
            }

            _operationProgressService.Complete(L("Adk.StatusDoneInstall"));
            RefreshStatus();
            _logger.LogInformation("ADK install completed successfully.");
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(Lf("Adk.ErrorInstall", ex.Message));
            _logger.LogError(ex, "ADK install failed.");
            throw;
        }
    }

    public async Task UninstallAdkAsync()
    {
        _logger.LogInformation("ADK uninstall requested.");
        if (!_operationProgressService.CanStartOperation)
        {
            _logger.LogWarning("ADK uninstall skipped because another operation is already in progress.");
            return;
        }

        // Refresh before evaluating uninstall eligibility to avoid stale cached state.
        RefreshStatus();
        if (!_isAdkInstalled)
        {
            _logger.LogInformation("ADK uninstall skipped because ADK is not installed.");
            return;
        }

        if (!_operationProgressService.TryStart(
                OperationKind.AdkUninstall,
                L("Adk.StatusStartUninstall"),
                0))
        {
            _logger.LogWarning("ADK uninstall skipped because another operation is already in progress.");
            return;
        }

        try
        {
            await EnsureInstallersDownloadedAsync(UninstallDownloadStart, UninstallDownloadEnd, forceDownload: true);
            await UninstallAdkInternalAsync(UninstallWinPeStart, UninstallWinPeEnd, UninstallAdkStart, UninstallAdkEnd);
            var synchronizationContext = SynchronizationContext.Current;
            var uninstallStateConfirmed = await WaitForInstallStateAsync(expectedInstalled: false, UninstallVerifyStart, UninstallVerifyEnd);
            if (!uninstallStateConfirmed)
            {
                StartDeferredStatusRefresh(expectedInstalled: false, synchronizationContext);
            }

            _operationProgressService.Complete(L("Adk.StatusDoneUninstall"));
            RefreshStatus();
            _logger.LogInformation("ADK uninstall completed successfully.");
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(Lf("Adk.ErrorUninstall", ex.Message));
            _logger.LogError(ex, "ADK uninstall failed.");
            throw;
        }
    }

    public async Task UpgradeAdkAsync()
    {
        _logger.LogInformation("ADK upgrade requested.");
        if (!_operationProgressService.CanStartOperation)
        {
            _logger.LogWarning("ADK upgrade skipped because another operation is already in progress.");
            return;
        }

        // Refresh before upgrade to make uninstall/upgrade branching deterministic.
        RefreshStatus();
        if (!_operationProgressService.TryStart(
                OperationKind.AdkUpgrade,
                L("Adk.StatusStartUpgrade"),
                0))
        {
            _logger.LogWarning("ADK upgrade skipped because another operation is already in progress.");
            return;
        }

        try
        {
            if (_isAdkInstalled)
            {
                UpdateOperationProgress(UpgradeUninstallPrepStart, L("Adk.StatusPreparingUninstall"));
                await EnsureInstallersDownloadedAsync(UpgradeUninstallPrepStart, UpgradeUninstallPrepEnd, forceDownload: true);
                await UninstallAdkInternalAsync(
                    UpgradeUninstallWinPeStart,
                    UpgradeUninstallWinPeEnd,
                    UpgradeUninstallAdkStart,
                    UpgradeUninstallAdkEnd);
            }
            else
            {
                UpdateOperationProgress(UpgradeDownloadStart, L("Adk.StatusNoExistingInstall"));
            }

            UpdateOperationProgress(UpgradeDownloadStart, L("Adk.StatusDownloadingUpgradeBinaries"));
            await EnsureInstallersDownloadedAsync(UpgradeDownloadStart, UpgradeDownloadEnd, forceDownload: false);
            await InstallAdkInternalAsync(
                UpgradeInstallAdkStart,
                UpgradeInstallAdkEnd,
                UpgradeInstallWinPeStart,
                UpgradeInstallWinPeEnd);
            var synchronizationContext = SynchronizationContext.Current;
            var upgradeStateConfirmed = await WaitForInstallStateAsync(expectedInstalled: true, UpgradeVerifyStart, UpgradeVerifyEnd);
            if (!upgradeStateConfirmed)
            {
                StartDeferredStatusRefresh(expectedInstalled: true, synchronizationContext);
            }

            _operationProgressService.Complete(L("Adk.StatusDoneUpgrade"));
            RefreshStatus();
            _logger.LogInformation("ADK upgrade completed successfully.");
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(Lf("Adk.ErrorUpgrade", ex.Message));
            _logger.LogError(ex, "ADK upgrade failed.");
            throw;
        }
    }

    /// <summary>
    /// Detects ADK installation by validating expected folders under KitsRoot10.
    /// </summary>
    private bool CheckAdkInstalled()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(AdkRegistryPath))
            {
                if (key == null)
                {
                    return false;
                }

                var kitsRoot = key.GetValue(AdkRegistryKey) as string;
                if (string.IsNullOrEmpty(kitsRoot) || !Directory.Exists(kitsRoot))
                {
                    return false;
                }

                var deploymentToolsPath = Path.Combine(kitsRoot, AdkBasePath, DeploymentToolsPath);
                var winPePath = Path.Combine(kitsRoot, AdkBasePath, WinPeEnvironmentPath);
                return Directory.Exists(deploymentToolsPath) && Directory.Exists(winPePath);
            }
        }
        catch
        {
            _logger.LogDebug("ADK installation check failed due to a registry or filesystem access error.");
            return false;
        }
    }

    /// <summary>
    /// Resolves installed ADK version using strict uninstall match first, then localized component fallback.
    /// </summary>
    private string? GetInstalledVersion()
    {
        try
        {
            var (installed, version) = TryGetAdkVersionFromUninstall();
            if (installed && !string.IsNullOrEmpty(version))
            {
                return version;
            }

            return TryGetAdkVersionFromComponentUninstallEntries();
        }
        catch
        {
            _logger.LogDebug("Unable to resolve installed ADK version from uninstall metadata.");
            return null;
        }
    }

    /// <summary>
    /// Fallback version resolution for environments where the top-level ADK uninstall entry is missing or localized.
    /// </summary>
    private static string? TryGetAdkVersionFromComponentUninstallEntries()
    {
        try
        {
            var registryPaths = new[]
            {
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };

            var candidateVersions = new List<string>();

            foreach (var path in registryPaths)
            {
                using var parentKey = Registry.LocalMachine.OpenSubKey(path);
                if (parentKey == null)
                {
                    continue;
                }

                foreach (var subKeyName in parentKey.GetSubKeyNames())
                {
                    using var subKey = parentKey.OpenSubKey(subKeyName);
                    var displayName = subKey?.GetValue("DisplayName") as string;
                    var displayVersion = subKey?.GetValue("DisplayVersion") as string;

                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(displayVersion))
                    {
                        continue;
                    }

                    // Fallback detection for localized environments where no top-level ADK entry is present.
                    var isDeploymentTools = displayName.Equals("Windows Deployment Tools", StringComparison.OrdinalIgnoreCase);
                    var isWinPeComponent =
                        ContainsTextIgnoreCaseAndDiacritics(displayName, "WinPE") ||
                        (ContainsTextIgnoreCaseAndDiacritics(displayName, "Windows PE") &&
                         (ContainsTextIgnoreCaseAndDiacritics(displayName, "Deployment") ||
                          ContainsTextIgnoreCaseAndDiacritics(displayName, "deploiement")));

                    if (isDeploymentTools || isWinPeComponent)
                    {
                        candidateVersions.Add(displayVersion);
                    }
                }
            }

            if (candidateVersions.Count == 0)
            {
                return null;
            }

            // Prefer the most frequent component version, then the highest parsed value.
            var selectedVersion = candidateVersions
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Version = group.Key,
                    Count = group.Count(),
                    Parsed = TryParseVersionSafe(group.Key)
                })
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.Parsed)
                .FirstOrDefault();

            return selectedVersion?.Version;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a version string safely and returns 0.0.0.0 when parsing fails.
    /// </summary>
    private static Version TryParseVersionSafe(string value)
    {
        return Version.TryParse(value, out var parsed)
            ? parsed
            : new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Performs case-insensitive, diacritic-insensitive text matching.
    /// </summary>
    private static bool ContainsTextIgnoreCaseAndDiacritics(string source, string value)
    {
        return CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            source,
            value,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>
    /// Keeps strict ADK uninstall entry matching by design.
    /// </summary>
    private static (bool Installed, string? Version) TryGetAdkVersionFromUninstall()
    {
        try
        {
            string? foundVersion = null;
            var registryPaths = new[]
            {
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };

            foreach (var path in registryPaths)
            {
                using (var parentKey = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (parentKey == null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in parentKey.GetSubKeyNames())
                    {
                        using (var subKey = parentKey.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName") as string;
                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Equals("Windows Assessment and Deployment Kit", StringComparison.OrdinalIgnoreCase))
                            {
                                foundVersion = subKey?.GetValue("DisplayVersion") as string;
                                // We found the ADK; return immediately.
                                return (true, foundVersion);
                            }
                        }
                    }
                }
            }
            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Validates the installed ADK version against the supported 24H2 baseline.
    /// </summary>
    private bool CheckAdkCompatible()
    {
        if (!_isAdkInstalled || string.IsNullOrEmpty(_installedVersion))
        {
            return false;
        }

        // Check whether the installed version is Windows 11 24H2 ADK (10.1.26100.*).
        try
        {
            var installedVersion = new Version(_installedVersion);
            var requiredVersion = new Version(Adk24H2Version);
            // Compare Major.Minor.Build to accept any revision (10.1.26100.*).
            return installedVersion.Major == requiredVersion.Major &&
                   installedVersion.Minor == requiredVersion.Minor &&
                   installedVersion.Build == requiredVersion.Build;
        }
        catch
        {
            // If version parsing fails, fallback to prefix matching.
            _logger.LogDebug("ADK version parsing failed for '{InstalledVersion}', applying prefix fallback.", _installedVersion);
            return _installedVersion.StartsWith("10.1.26100.", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Updates operation status while enforcing monotonic progress during active operations.
    /// </summary>
    private void UpdateOperationProgress(int progress, string? status)
    {
        _operationProgressService.Report(progress, status);
    }

    private string L(string key)
    {
        return _localizationService.Strings[key];
    }

    private string Lf(string key, params object[] args)
    {
        return string.Format(_localizationService.CurrentCulture, _localizationService.Strings[key], args);
    }

    private void OnGlobalProgressChanged(object? sender, EventArgs e)
    {
        OperationProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsAdkOperation(OperationKind? kind)
    {
        return kind is OperationKind.AdkDownload
            or OperationKind.AdkInstall
            or OperationKind.AdkUpgrade
            or OperationKind.AdkUninstall;
    }

    /// <summary>
    /// Ensures ADK and WinPE setup binaries are present in shared cache storage, downloading when required.
    /// </summary>
    private async Task EnsureInstallersDownloadedAsync(int progressStart, int progressEnd, bool forceDownload)
    {
        if (progressEnd < progressStart)
        {
            throw new ArgumentOutOfRangeException(nameof(progressEnd), "The end progress must be greater than or equal to the start progress.");
        }

        string installerCacheDirectoryPath = WinPeDefaults.GetInstallerCacheDirectoryPath();
        Directory.CreateDirectory(installerCacheDirectoryPath);

        var adkInstallerPath = Path.Combine(installerCacheDirectoryPath, "adksetup.exe");
        var winPeInstallerPath = Path.Combine(installerCacheDirectoryPath, "adkwinpesetup.exe");
        var midpoint = progressStart + ((progressEnd - progressStart) / 2);
        var adkInstallerName = L("Adk.ComponentInstaller");
        var winPeInstallerName = L("Adk.ComponentWinPeInstaller");
        _logger.LogInformation(
            "Ensuring ADK installers are available. ForceDownload={ForceDownload}, InstallerCacheDirectoryPath={InstallerCacheDirectoryPath}, AdkPath={AdkInstallerPath}, WinPePath={WinPeInstallerPath}",
            forceDownload,
            installerCacheDirectoryPath,
            adkInstallerPath,
            winPeInstallerPath);

        if (forceDownload || !File.Exists(adkInstallerPath))
        {
            UpdateOperationProgress(progressStart, L("Adk.StatusDownloadingAdkInstaller"));
            await DownloadFileAsync(AdkDownloadUrl, adkInstallerPath, progressStart, midpoint, adkInstallerName);
        }
        else
        {
            UpdateOperationProgress(midpoint, L("Adk.StatusUsingCachedAdkInstaller"));
        }

        if (forceDownload || !File.Exists(winPeInstallerPath))
        {
            UpdateOperationProgress(midpoint, L("Adk.StatusDownloadingWinPeInstaller"));
            await DownloadFileAsync(WinPeAddonDownloadUrl, winPeInstallerPath, midpoint, progressEnd, winPeInstallerName);
        }
        else
        {
            UpdateOperationProgress(progressEnd, L("Adk.StatusUsingCachedWinPeInstaller"));
        }

        _downloadedAdkInstallerPath = adkInstallerPath;
        _downloadedWinPeInstallerPath = winPeInstallerPath;
        _logger.LogDebug("Installer paths cached. AdkInstallerPath={AdkInstallerPath}, WinPeInstallerPath={WinPeInstallerPath}",
            _downloadedAdkInstallerPath,
            _downloadedWinPeInstallerPath);
    }

    /// <summary>
    /// Downloads a file and maps per-file progress into the caller's global progress range.
    /// </summary>
    private async Task DownloadFileAsync(string url, string filePath, int progressStart, int progressEnd, string componentName)
    {
        _logger.LogInformation("Downloading installer component {ComponentName} from {Url} to {FilePath}.", componentName, url, filePath);
        using (var httpClient = new HttpClient())
        {
            httpClient.Timeout = TimeSpan.FromMinutes(30);
            using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var fileProgress = (int)((totalRead * 100) / totalBytes);
                            var overallProgress = progressStart + ((progressEnd - progressStart) * fileProgress / 100);
                            UpdateOperationProgress(overallProgress, Lf("Adk.StatusDownloadingComponentPercent", componentName, fileProgress));
                        }
                    }
                }
            }
        }

        UpdateOperationProgress(progressEnd, Lf("Adk.StatusComponentDownloadComplete", componentName));
        _logger.LogInformation("Installer component download completed for {ComponentName}.", componentName);
    }

    /// <summary>
    /// Runs ADK and WinPE setup in install mode.
    /// </summary>
    private async Task InstallAdkInternalAsync(int adkProgressStart, int adkProgressEnd, int winPeProgressStart, int winPeProgressEnd)
    {
        EnsureInstallersAvailable();
        UpdateOperationProgress(adkProgressStart, L("Adk.StatusInstallingDeploymentTools"));
        await RunProcessOrThrowAsync(_downloadedAdkInstallerPath!, AdkInstallArguments, L("Adk.ComponentInstaller"));
        UpdateOperationProgress(adkProgressEnd, L("Adk.StatusInstalledDeploymentTools"));

        UpdateOperationProgress(winPeProgressStart, L("Adk.StatusInstallingWinPe"));
        await RunProcessOrThrowAsync(_downloadedWinPeInstallerPath!, WinPeInstallArguments, L("Adk.ComponentWinPeInstaller"));
        UpdateOperationProgress(winPeProgressEnd, L("Adk.StatusInstalledWinPe"));
    }

    /// <summary>
    /// Runs WinPE and ADK setup in uninstall mode.
    /// </summary>
    private async Task UninstallAdkInternalAsync(int winPeProgressStart, int winPeProgressEnd, int adkProgressStart, int adkProgressEnd)
    {
        EnsureInstallersAvailable();
        UpdateOperationProgress(winPeProgressStart, L("Adk.StatusUninstallingWinPe"));
        await RunProcessOrThrowAsync(_downloadedWinPeInstallerPath!, WinPeUninstallArguments, L("Adk.ComponentWinPeInstaller"));
        UpdateOperationProgress(winPeProgressEnd, L("Adk.StatusUninstalledWinPe"));

        UpdateOperationProgress(adkProgressStart, L("Adk.StatusUninstallingDeploymentTools"));
        await RunProcessOrThrowAsync(_downloadedAdkInstallerPath!, AdkUninstallArguments, L("Adk.ComponentInstaller"));
        UpdateOperationProgress(adkProgressEnd, L("Adk.StatusUninstalledDeploymentTools"));
    }

    /// <summary>
    /// Verifies that downloaded installer paths exist before invoking setup processes.
    /// </summary>
    private void EnsureInstallersAvailable()
    {
        if (string.IsNullOrWhiteSpace(_downloadedAdkInstallerPath) || !File.Exists(_downloadedAdkInstallerPath))
        {
            throw new InvalidOperationException("The ADK installer executable is not available.");
        }

        if (string.IsNullOrWhiteSpace(_downloadedWinPeInstallerPath) || !File.Exists(_downloadedWinPeInstallerPath))
        {
            throw new InvalidOperationException("The WinPE Add-on installer executable is not available.");
        }
    }

    /// <summary>
    /// Executes an external process and throws on startup or non-zero exit.
    /// </summary>
    private static async Task RunProcessOrThrowAsync(string executablePath, string arguments, string operationName)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"{operationName} executable was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start {operationName}.");
        }

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{operationName} failed with exit code {process.ExitCode}.");
        }
    }

    /// <summary>
    /// Polls installation state until expected value is reached or timeout expires.
    /// Returns false on timeout without throwing to support soft verification behavior.
    /// </summary>
    private async Task<bool> WaitForInstallStateAsync(
        bool expectedInstalled,
        int progressStart,
        int progressEnd,
        TimeSpan? timeout = null,
        int pollDelayMs = InstallStatePollDelayMs,
        bool reportProgress = true)
    {
        if (pollDelayMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pollDelayMs), "Poll delay must be a positive number of milliseconds.");
        }

        var effectiveTimeout = timeout ?? InstallStateWaitTimeout;
        var maxRetries = Math.Max(1, (int)Math.Ceiling(effectiveTimeout.TotalMilliseconds / pollDelayMs));
        string expectedStateText = expectedInstalled
            ? L("Adk.StateInstalled")
            : L("Adk.StateUninstalled");

        for (int retry = 0; retry < maxRetries; retry++)
        {
            var currentInstalled = CheckAdkInstalled();
            if (currentInstalled == expectedInstalled)
            {
                if (reportProgress)
                {
                    UpdateOperationProgress(progressEnd, Lf("Adk.StatusStateReached", expectedStateText));
                }

                return true;
            }

            if (reportProgress)
            {
                var progress = progressStart + ((progressEnd - progressStart) * (retry + 1) / maxRetries);
                UpdateOperationProgress(progress, Lf("Adk.StatusWaitingForState", expectedStateText));
            }

            await Task.Delay(pollDelayMs);
        }

        if (CheckAdkInstalled() == expectedInstalled)
        {
            if (reportProgress)
            {
                UpdateOperationProgress(progressEnd, Lf("Adk.StatusStateReached", expectedStateText));
            }

            return true;
        }

        if (reportProgress)
        {
            UpdateOperationProgress(progressEnd, L("Adk.StatusVerificationPending"));
        }

        _logger.LogWarning("Timed out while waiting for ADK install state transition. ExpectedInstalled={ExpectedInstalled}", expectedInstalled);
        return false;
    }

    /// <summary>
    /// Triggers a background state reconciliation when immediate verification times out.
    /// </summary>
    private void StartDeferredStatusRefresh(bool expectedInstalled, SynchronizationContext? synchronizationContext)
    {
        // Fire-and-forget reconciliation in case registry state propagation lags behind setup exit.
        _logger.LogInformation("Starting deferred ADK status refresh. ExpectedInstalled={ExpectedInstalled}", expectedInstalled);
        _ = Task.Run(async () =>
        {
            await WaitForInstallStateAsync(
                expectedInstalled,
                progressStart: 100,
                progressEnd: 100,
                timeout: DeferredInstallStateWaitTimeout,
                pollDelayMs: InstallStatePollDelayMs,
                reportProgress: false).ConfigureAwait(false);

            if (synchronizationContext != null)
            {
                synchronizationContext.Post(_ => RefreshStatus(), null);
            }
            else
            {
                RefreshStatus();
            }
        });
    }
}
