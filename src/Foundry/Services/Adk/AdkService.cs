using Microsoft.Win32;
using System.Diagnostics;
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
    private const int InstallStatePollDelayMs = 1000;
    private static readonly TimeSpan InstallStateWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeferredInstallStateWaitTimeout = TimeSpan.FromMinutes(15);

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
    private bool _isOperationInProgress;
    private int _operationProgress;
    private string? _operationStatus;
    private string? _downloadedAdkInstallerPath;
    private string? _downloadedWinPeInstallerPath;

    public bool IsAdkInstalled => _isAdkInstalled;
    public bool IsAdkCompatible => _isAdkCompatible;
    public string? InstalledVersion => _installedVersion;
    public bool IsOperationInProgress => _isOperationInProgress;
    public int OperationProgress => _operationProgress;
    public string? OperationStatus => _operationStatus;

    public event EventHandler? AdkStatusChanged;
    public event EventHandler? OperationProgressChanged;

    public AdkService()
    {
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        _isAdkInstalled = CheckAdkInstalled();
        _installedVersion = GetInstalledVersion();
        _isAdkCompatible = CheckAdkCompatible();
        AdkStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DownloadAdkAsync()
    {
        if (_isOperationInProgress)
        {
            return;
        }

        try
        {
            SetOperationInProgress(true, "Downloading ADK installers...", DownloadDefaultStart);
            await EnsureInstallersDownloadedAsync(DownloadDefaultStart, DownloadDefaultEnd, forceDownload: true);

            UpdateOperationProgress(100, "Download complete");
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(OperationProgress, $"Download failed: {ex.Message}");
            throw;
        }
        finally
        {
            SetOperationInProgress(false, null, 0);
        }
    }

    public async Task InstallAdkAsync()
    {
        if (_isOperationInProgress)
        {
            return;
        }

        try
        {
            SetOperationInProgress(true, "Installing ADK and WinPE Add-on...", 0);
            await EnsureInstallersDownloadedAsync(InstallDownloadStart, InstallDownloadEnd, forceDownload: false);
            await InstallAdkInternalAsync(InstallAdkStart, InstallAdkEnd, InstallWinPeStart, InstallWinPeEnd);
            var synchronizationContext = SynchronizationContext.Current;
            var installStateConfirmed = await WaitForInstallStateAsync(expectedInstalled: true, InstallVerifyStart, InstallVerifyEnd);
            if (!installStateConfirmed)
            {
                StartDeferredStatusRefresh(expectedInstalled: true, synchronizationContext);
            }

            UpdateOperationProgress(100, "Installation complete");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(OperationProgress, $"Installation failed: {ex.Message}");
            throw;
        }
        finally
        {
            SetOperationInProgress(false, null, 0);
        }
    }

    public async Task UninstallAdkAsync()
    {
        if (_isOperationInProgress)
        {
            return;
        }

        if (!_isAdkInstalled)
        {
            return;
        }

        try
        {
            SetOperationInProgress(true, "Uninstalling ADK...", 0);
            await EnsureInstallersDownloadedAsync(UninstallDownloadStart, UninstallDownloadEnd, forceDownload: true);
            await UninstallAdkInternalAsync(UninstallWinPeStart, UninstallWinPeEnd, UninstallAdkStart, UninstallAdkEnd);
            var synchronizationContext = SynchronizationContext.Current;
            var uninstallStateConfirmed = await WaitForInstallStateAsync(expectedInstalled: false, UninstallVerifyStart, UninstallVerifyEnd);
            if (!uninstallStateConfirmed)
            {
                StartDeferredStatusRefresh(expectedInstalled: false, synchronizationContext);
            }

            UpdateOperationProgress(100, "Uninstallation complete");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(OperationProgress, $"Uninstallation failed: {ex.Message}");
            throw;
        }
        finally
        {
            SetOperationInProgress(false, null, 0);
        }
    }

    public async Task UpgradeAdkAsync()
    {
        if (_isOperationInProgress)
        {
            return;
        }

        try
        {
            SetOperationInProgress(true, "Upgrading ADK...", 0);

            if (_isAdkInstalled)
            {
                UpdateOperationProgress(UpgradeUninstallPrepStart, "Preparing uninstall binaries...");
                await EnsureInstallersDownloadedAsync(UpgradeUninstallPrepStart, UpgradeUninstallPrepEnd, forceDownload: true);
                await UninstallAdkInternalAsync(
                    UpgradeUninstallWinPeStart,
                    UpgradeUninstallWinPeEnd,
                    UpgradeUninstallAdkStart,
                    UpgradeUninstallAdkEnd);
            }
            else
            {
                UpdateOperationProgress(UpgradeDownloadStart, "No existing ADK installation detected.");
            }

            UpdateOperationProgress(UpgradeDownloadStart, "Downloading upgrade binaries...");
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

            UpdateOperationProgress(100, "Upgrade complete");
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(OperationProgress, $"Upgrade failed: {ex.Message}");
            throw;
        }
        finally
        {
            SetOperationInProgress(false, null, 0);
        }
    }

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
            return false;
        }
    }

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
            return null;
        }
    }

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
                        displayName.Contains("WinPE", StringComparison.OrdinalIgnoreCase) ||
                        (displayName.Contains("Windows PE", StringComparison.OrdinalIgnoreCase) &&
                         (displayName.Contains("Deployment", StringComparison.OrdinalIgnoreCase) ||
                          displayName.Contains("deploiement", StringComparison.OrdinalIgnoreCase) ||
                          displayName.Contains("déploiement", StringComparison.OrdinalIgnoreCase)));

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

    private static Version TryParseVersionSafe(string value)
    {
        return Version.TryParse(value, out var parsed)
            ? parsed
            : new Version(0, 0, 0, 0);
    }

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
            return _installedVersion.StartsWith("10.1.26100.", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SetOperationInProgress(bool inProgress, string? status, int progress)
    {
        _isOperationInProgress = inProgress;
        _operationStatus = status;
        _operationProgress = Math.Clamp(progress, 0, 100);
        OperationProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOperationProgress(int progress, string? status)
    {
        var normalizedProgress = Math.Clamp(progress, 0, 100);
        _operationProgress = _isOperationInProgress
            ? Math.Max(_operationProgress, normalizedProgress)
            : normalizedProgress;
        _operationStatus = status;
        OperationProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureInstallersDownloadedAsync(int progressStart, int progressEnd, bool forceDownload)
    {
        if (progressEnd < progressStart)
        {
            throw new ArgumentOutOfRangeException(nameof(progressEnd), "The end progress must be greater than or equal to the start progress.");
        }

        var tempPath = Path.GetTempPath();
        var adkInstallerPath = Path.Combine(tempPath, "adksetup.exe");
        var winPeInstallerPath = Path.Combine(tempPath, "adkwinpesetup.exe");
        var midpoint = progressStart + ((progressEnd - progressStart) / 2);

        if (forceDownload || !File.Exists(adkInstallerPath))
        {
            UpdateOperationProgress(progressStart, "Downloading ADK installer...");
            await DownloadFileAsync(AdkDownloadUrl, adkInstallerPath, progressStart, midpoint, "ADK installer");
        }
        else
        {
            UpdateOperationProgress(midpoint, "Using cached ADK installer.");
        }

        if (forceDownload || !File.Exists(winPeInstallerPath))
        {
            UpdateOperationProgress(midpoint, "Downloading WinPE Add-on installer...");
            await DownloadFileAsync(WinPeAddonDownloadUrl, winPeInstallerPath, midpoint, progressEnd, "WinPE Add-on installer");
        }
        else
        {
            UpdateOperationProgress(progressEnd, "Using cached WinPE Add-on installer.");
        }

        _downloadedAdkInstallerPath = adkInstallerPath;
        _downloadedWinPeInstallerPath = winPeInstallerPath;
    }

    private async Task DownloadFileAsync(string url, string filePath, int progressStart, int progressEnd, string componentName)
    {
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
                            UpdateOperationProgress(overallProgress, $"Downloading {componentName}... {fileProgress}%");
                        }
                    }
                }
            }
        }

        UpdateOperationProgress(progressEnd, $"{componentName} download completed.");
    }

    private async Task InstallAdkInternalAsync(int adkProgressStart, int adkProgressEnd, int winPeProgressStart, int winPeProgressEnd)
    {
        EnsureInstallersAvailable();
        UpdateOperationProgress(adkProgressStart, "Installing ADK Deployment Tools...");
        await RunProcessOrThrowAsync(_downloadedAdkInstallerPath!, AdkInstallArguments, "ADK installer");
        UpdateOperationProgress(adkProgressEnd, "ADK Deployment Tools installation completed.");

        UpdateOperationProgress(winPeProgressStart, "Installing WinPE Add-on...");
        await RunProcessOrThrowAsync(_downloadedWinPeInstallerPath!, WinPeInstallArguments, "WinPE Add-on installer");
        UpdateOperationProgress(winPeProgressEnd, "WinPE Add-on installation completed.");
    }

    private async Task UninstallAdkInternalAsync(int winPeProgressStart, int winPeProgressEnd, int adkProgressStart, int adkProgressEnd)
    {
        EnsureInstallersAvailable();
        UpdateOperationProgress(winPeProgressStart, "Uninstalling WinPE Add-on...");
        await RunProcessOrThrowAsync(_downloadedWinPeInstallerPath!, WinPeUninstallArguments, "WinPE Add-on installer");
        UpdateOperationProgress(winPeProgressEnd, "WinPE Add-on uninstallation completed.");

        UpdateOperationProgress(adkProgressStart, "Uninstalling ADK Deployment Tools...");
        await RunProcessOrThrowAsync(_downloadedAdkInstallerPath!, AdkUninstallArguments, "ADK installer");
        UpdateOperationProgress(adkProgressEnd, "ADK Deployment Tools uninstallation completed.");
    }

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

    private async Task<bool> WaitForInstallStateAsync(
        bool expectedInstalled,
        int progressStart,
        int progressEnd,
        TimeSpan? timeout = null,
        int pollDelayMs = InstallStatePollDelayMs,
        bool reportProgress = true)
    {
        var effectiveTimeout = timeout ?? InstallStateWaitTimeout;
        var maxRetries = Math.Max(1, (int)Math.Ceiling(effectiveTimeout.TotalMilliseconds / pollDelayMs));
        string expectedStateText = expectedInstalled ? "installed" : "uninstalled";

        for (int retry = 0; retry < maxRetries; retry++)
        {
            var currentInstalled = CheckAdkInstalled();
            if (currentInstalled == expectedInstalled)
            {
                if (reportProgress)
                {
                    UpdateOperationProgress(progressEnd, $"ADK is now {expectedStateText}.");
                }

                return true;
            }

            if (reportProgress)
            {
                var progress = progressStart + ((progressEnd - progressStart) * (retry + 1) / maxRetries);
                UpdateOperationProgress(progress, $"Waiting for ADK to be {expectedStateText}...");
            }

            await Task.Delay(pollDelayMs);
        }

        if (CheckAdkInstalled() == expectedInstalled)
        {
            if (reportProgress)
            {
                UpdateOperationProgress(progressEnd, $"ADK is now {expectedStateText}.");
            }

            return true;
        }

        if (reportProgress)
        {
            UpdateOperationProgress(progressEnd, $"Installer finished, but ADK verification is still pending.");
        }

        return false;
    }

    private void StartDeferredStatusRefresh(bool expectedInstalled, SynchronizationContext? synchronizationContext)
    {
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
