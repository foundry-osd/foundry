using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Foundry.Services.Adk;

/// <summary>
/// Implementation of the Windows ADK management service.
/// </summary>
public sealed class AdkService : IAdkService
{
    private const string Adk24H2Version = "10.1.26100.1"; // Windows 11 24H2 ADK version
    private const string AdkRegistryPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";
    private const string AdkRegistryKey = "KitsRoot10";
    private const string AdkDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2289980"; // Windows ADK for Windows 11, version 24H2
    private const string WinPeAddonDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2289981"; // WinPE Add-on for Windows 11, version 24H2
    private const string AdkBasePath = "Assessment and Deployment Kit";
    private const string DeploymentToolsPath = "Deployment Tools";
    private const string WinPeEnvironmentPath = "Windows Preinstallation Environment";
    
    private const int AdkDownloadProgressStart = 0;
    private const int AdkDownloadProgressEnd = 50;
    private const int WinPeDownloadProgressStart = 50;
    private const int WinPeDownloadProgressEnd = 100;

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
            SetOperationInProgress(true, "Downloading ADK installers...", 0);

            var tempPath = Path.GetTempPath();
            var adkInstallerPath = Path.Combine(tempPath, "adksetup.exe");
            var winPeInstallerPath = Path.Combine(tempPath, "adkwinpesetup.exe");

            // Download ADK installer
            UpdateOperationProgress(AdkDownloadProgressStart, "Downloading ADK installer...");
            await DownloadFileAsync(AdkDownloadUrl, adkInstallerPath, AdkDownloadProgressStart, AdkDownloadProgressEnd, "ADK installer");
            _downloadedAdkInstallerPath = adkInstallerPath;

            // Download WinPE Add-on installer
            UpdateOperationProgress(WinPeDownloadProgressStart, "Downloading WinPE Add-on installer...");
            await DownloadFileAsync(WinPeAddonDownloadUrl, winPeInstallerPath, WinPeDownloadProgressStart, WinPeDownloadProgressEnd, "WinPE Add-on installer");
            _downloadedWinPeInstallerPath = winPeInstallerPath;

            UpdateOperationProgress(100, "Download complete");
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(0, $"Download failed: {ex.Message}");
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

            // Download if not already downloaded
            if (string.IsNullOrEmpty(_downloadedAdkInstallerPath) || !File.Exists(_downloadedAdkInstallerPath) ||
                string.IsNullOrEmpty(_downloadedWinPeInstallerPath) || !File.Exists(_downloadedWinPeInstallerPath))
            {
                await DownloadAdkInternalAsync();
            }

            if (string.IsNullOrEmpty(_downloadedAdkInstallerPath) || string.IsNullOrEmpty(_downloadedWinPeInstallerPath))
            {
                throw new InvalidOperationException("ADK installers not available");
            }

            UpdateOperationProgress(5, "Installing ADK (Deployment Tools)...");

            // Install ADK first with Deployment Tools feature
            var adkStartInfo = new ProcessStartInfo
            {
                FileName = _downloadedAdkInstallerPath,
                Arguments = "/quiet /features OptionId.DeploymentTools",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(adkStartInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start ADK installer");
                }

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"ADK installation failed with exit code {process.ExitCode}");
                }
            }

            UpdateOperationProgress(50, "Installing WinPE Add-on...");

            // Install WinPE Add-on second with Windows Preinstallation Environment feature
            var winPeStartInfo = new ProcessStartInfo
            {
                FileName = _downloadedWinPeInstallerPath,
                Arguments = "/quiet /features OptionId.WindowsPreinstallationEnvironment",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(winPeStartInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start WinPE Add-on installer");
                }

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"WinPE Add-on installation failed with exit code {process.ExitCode}");
                }
            }

            UpdateOperationProgress(100, "Installation complete");
            
            // Wait for registry to be updated with retry mechanism
            await WaitForRegistryUpdateAsync();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(0, $"Installation failed: {ex.Message}");
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
            await UninstallAdkInternalAsync();
            UpdateOperationProgress(100, "Uninstallation complete");
            
            // Wait for registry to be updated with retry mechanism
            await WaitForRegistryUpdateAsync();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(0, $"Uninstallation failed: {ex.Message}");
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
                UpdateOperationProgress(10, "Uninstalling current ADK version...");
                await UninstallAdkInternalAsync();
            }

            UpdateOperationProgress(40, "Downloading new ADK version...");
            await DownloadAdkInternalAsync();

            UpdateOperationProgress(60, "Installing new ADK version...");
            await InstallAdkInternalAsync();

            UpdateOperationProgress(100, "Upgrade complete");
            
            // Wait for registry to be updated with retry mechanism
            await WaitForRegistryUpdateAsync();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            UpdateOperationProgress(0, $"Upgrade failed: {ex.Message}");
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

                // Verify both ADK and WinPE Add-on are installed
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
            var adkPath = GetAdkInstallPath();
            if (string.IsNullOrEmpty(adkPath))
            {
                return null;
            }

            // Try to read version from the registry
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots"))
            {
                if (key != null)
                {
                    var version = key.GetValue("KitsRoot10Version") as string;
                    if (!string.IsNullOrEmpty(version))
                    {
                        return version.Trim('\\');
                    }
                }
            }

            // Unable to determine version - return null
            return null;
        }
        catch
        {
            return null;
        }
    }

    private bool CheckAdkCompatible()
    {
        if (!_isAdkInstalled || string.IsNullOrEmpty(_installedVersion))
        {
            return false;
        }

        // Check if installed version is exactly Windows 11 24H2 ADK (10.1.26100.1)
        try
        {
            var installedVersion = new Version(_installedVersion);
            var requiredVersion = new Version(Adk24H2Version);
            return installedVersion == requiredVersion;
        }
        catch
        {
            // If version parsing fails, do an exact string comparison
            return string.Equals(_installedVersion, Adk24H2Version, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string? GetAdkInstallPath()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(AdkRegistryPath))
            {
                return key?.GetValue(AdkRegistryKey) as string;
            }
        }
        catch
        {
            return null;
        }
    }

    private void SetOperationInProgress(bool inProgress, string? status, int progress)
    {
        _isOperationInProgress = inProgress;
        _operationStatus = status;
        _operationProgress = progress;
        OperationProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOperationProgress(int progress, string? status)
    {
        _operationProgress = progress;
        _operationStatus = status;
        OperationProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DownloadAdkInternalAsync()
    {
        var tempPath = Path.GetTempPath();
        var adkInstallerPath = Path.Combine(tempPath, "adksetup.exe");
        var winPeInstallerPath = Path.Combine(tempPath, "adkwinpesetup.exe");

        // Download ADK installer
        UpdateOperationProgress(AdkDownloadProgressStart, "Downloading ADK installer...");
        await DownloadFileAsync(AdkDownloadUrl, adkInstallerPath, AdkDownloadProgressStart, AdkDownloadProgressEnd, "ADK installer");
        _downloadedAdkInstallerPath = adkInstallerPath;

        // Download WinPE Add-on installer
        UpdateOperationProgress(WinPeDownloadProgressStart, "Downloading WinPE Add-on installer...");
        await DownloadFileAsync(WinPeAddonDownloadUrl, winPeInstallerPath, WinPeDownloadProgressStart, WinPeDownloadProgressEnd, "WinPE Add-on installer");
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
    }

    private async Task InstallAdkInternalAsync()
    {
        if (string.IsNullOrEmpty(_downloadedAdkInstallerPath) || string.IsNullOrEmpty(_downloadedWinPeInstallerPath))
        {
            throw new InvalidOperationException("ADK installers not available");
        }

        // Install ADK first with Deployment Tools feature
        var adkStartInfo = new ProcessStartInfo
        {
            FileName = _downloadedAdkInstallerPath,
            Arguments = "/quiet /features OptionId.DeploymentTools",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(adkStartInfo))
        {
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start ADK installer");
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADK installation failed with exit code {process.ExitCode}");
            }
        }

        // Install WinPE Add-on second with Windows Preinstallation Environment feature
        var winPeStartInfo = new ProcessStartInfo
        {
            FileName = _downloadedWinPeInstallerPath,
            Arguments = "/quiet /features OptionId.WindowsPreinstallationEnvironment",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(winPeStartInfo))
        {
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start WinPE Add-on installer");
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"WinPE Add-on installation failed with exit code {process.ExitCode}");
            }
        }
    }

    private async Task UninstallAdkInternalAsync()
    {
        UpdateOperationProgress(10, "Uninstalling WinPE Add-on...");

        var adkPath = GetAdkInstallPath();
        if (!string.IsNullOrEmpty(adkPath))
        {
            // Uninstall WinPE Add-on first (if installed)
            var winPeUninstallerPath = Path.Combine(adkPath, AdkBasePath, WinPeEnvironmentPath, "uninstall.exe");
            
            if (File.Exists(winPeUninstallerPath))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = winPeUninstallerPath,
                    Arguments = "/quiet",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                    }
                }
            }

            UpdateOperationProgress(50, "Uninstalling ADK...");

            // Uninstall ADK second (if installed)
            var adkUninstallerPath = Path.Combine(adkPath, AdkBasePath, DeploymentToolsPath, "uninstall.exe");
            
            if (File.Exists(adkUninstallerPath))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adkUninstallerPath,
                    Arguments = "/quiet",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                    }
                }
            }
        }
    }

    private async Task WaitForRegistryUpdateAsync()
    {
        const int maxRetries = 10;
        const int delayMs = 500;
        
        // Capture the expected state after the operation completes (inverse of current state)
        // If currently installed, we expect it to be uninstalled after the operation, and vice versa
        var expectedState = !_isAdkInstalled;

        for (int i = 0; i < maxRetries; i++)
        {
            await Task.Delay(delayMs);
            
            // Check if registry has been updated to the expected state
            var currentInstallState = CheckAdkInstalled();
            if (currentInstallState == expectedState)
            {
                // Registry state changed to expected value, update is complete
                return;
            }
        }

        // Timeout reached, continue anyway
    }
}
