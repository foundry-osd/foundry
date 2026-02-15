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
    private const string AdkDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2271337"; // Windows ADK for Windows 11, version 24H2

    private bool _isAdkInstalled;
    private bool _isAdkCompatible;
    private string? _installedVersion;
    private bool _isOperationInProgress;
    private int _operationProgress;
    private string? _operationStatus;
    private string? _downloadedInstallerPath;

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
            SetOperationInProgress(true, "Downloading ADK installer...", 0);

            var tempPath = Path.GetTempPath();
            var installerPath = Path.Combine(tempPath, "adksetup.exe");

            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(30);
                using (var response = await httpClient.GetAsync(AdkDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
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
                                var progress = (int)((totalRead * 100) / totalBytes);
                                UpdateOperationProgress(progress, $"Downloading ADK installer... {progress}%");
                            }
                        }
                    }
                }
            }

            _downloadedInstallerPath = installerPath;
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
            SetOperationInProgress(true, "Installing ADK...", 0);

            // Download if not already downloaded
            if (string.IsNullOrEmpty(_downloadedInstallerPath) || !File.Exists(_downloadedInstallerPath))
            {
                await DownloadAdkAsync();
            }

            if (string.IsNullOrEmpty(_downloadedInstallerPath))
            {
                throw new InvalidOperationException("ADK installer not available");
            }

            UpdateOperationProgress(10, "Starting ADK installation...");

            // Install ADK with quiet mode and required features
            var startInfo = new ProcessStartInfo
            {
                FileName = _downloadedInstallerPath,
                Arguments = "/quiet /features OptionId.DeploymentTools OptionId.WindowsPreinstallationEnvironment",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            UpdateOperationProgress(20, "Installing ADK components...");

            using (var process = Process.Start(startInfo))
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

            UpdateOperationProgress(10, "Starting ADK uninstallation...");

            // Find the ADK uninstaller
            var adkPath = GetAdkInstallPath();
            if (!string.IsNullOrEmpty(adkPath))
            {
                var uninstallerPath = Path.Combine(adkPath, "Assessment and Deployment Kit", "Deployment Tools", "uninstall.exe");
                
                if (File.Exists(uninstallerPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = uninstallerPath,
                        Arguments = "/quiet",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    UpdateOperationProgress(20, "Removing ADK components...");

                    using (var process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                        }
                    }
                }
            }

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
                await UninstallAdkAsync();
            }

            UpdateOperationProgress(50, "Installing new ADK version...");
            await InstallAdkAsync();

            UpdateOperationProgress(100, "Upgrade complete");
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
                return !string.IsNullOrEmpty(kitsRoot) && Directory.Exists(kitsRoot);
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

        // For Windows 11 24H2, we need ADK version 10.1.26100.1 or later
        try
        {
            var installedVersion = new Version(_installedVersion);
            var requiredVersion = new Version(Adk24H2Version);
            return installedVersion >= requiredVersion;
        }
        catch
        {
            // If version parsing fails, do a simple string comparison
            return string.Compare(_installedVersion, Adk24H2Version, StringComparison.Ordinal) >= 0;
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

    private async Task WaitForRegistryUpdateAsync()
    {
        const int maxRetries = 10;
        const int delayMs = 500;

        for (int i = 0; i < maxRetries; i++)
        {
            await Task.Delay(delayMs);
            
            // Check if registry has been updated
            var currentInstallState = CheckAdkInstalled();
            if (currentInstallState != _isAdkInstalled)
            {
                // Registry state changed, update is complete
                return;
            }
        }

        // Timeout reached, continue anyway
    }
}
