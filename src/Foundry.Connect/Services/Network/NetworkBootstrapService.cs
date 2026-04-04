using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

public sealed class NetworkBootstrapService : INetworkBootstrapService
{
    private const string EnterpriseSecurityType = "Enterprise";

    private readonly FoundryConnectConfiguration _configuration;
    private readonly IConnectConfigurationService _configurationService;
    private readonly ILogger<NetworkBootstrapService> _logger;

    public NetworkBootstrapService(
        FoundryConnectConfiguration configuration,
        IConnectConfigurationService configurationService,
        ILogger<NetworkBootstrapService> logger)
    {
        _configuration = configuration;
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
    {
        List<string> messages = [];

        if (_configuration.Dot1x.IsEnabled)
        {
            messages.Add(await ApplyWiredDot1xProfileAsync(cancellationToken).ConfigureAwait(false));
        }

        if (_configuration.Capabilities.WifiProvisioned && _configuration.Wifi.IsEnabled)
        {
            messages.Add(await EnsureWifiProfileAsync(cancellationToken).ConfigureAwait(false));
        }

        return messages.Count == 0
            ? "No provisioned network bootstrap actions were requested."
            : string.Join(" ", messages.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    public async Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.Capabilities.WifiProvisioned || !_configuration.Wifi.IsEnabled)
        {
            return "Wi-Fi is not provisioned for this image.";
        }

        string ensureMessage = await EnsureWifiProfileAsync(cancellationToken).ConfigureAwait(false);
        string? profileName = ResolveWifiProfileName();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return $"{ensureMessage} No Wi-Fi profile is available to connect.";
        }

        string arguments = string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid)
            ? $"wlan connect name=\"{profileName}\""
            : $"wlan connect name=\"{profileName}\" ssid=\"{_configuration.Wifi.Ssid.Trim()}\"";

        ProcessExecutionResult result = await ExecuteProcessAsync("netsh", arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return $"{ensureMessage} Wi-Fi connection attempt started for '{profileName}'.";
        }

        _logger.LogWarning(
            "Wi-Fi connection attempt failed. ExitCode={ExitCode}, StdOut={StdOut}, StdErr={StdErr}",
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
        return $"{ensureMessage} Wi-Fi connection attempt failed: {CollapseError(result)}";
    }

    public async Task<string> ConnectWifiNetworkAsync(string ssid, string authentication, string? passphrase, CancellationToken cancellationToken)
    {
        if (!_configuration.Capabilities.WifiProvisioned && !Debugger.IsAttached)
        {
            return "Wi-Fi support is not provisioned for this image.";
        }

        string trimmedSsid = ssid?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedSsid))
        {
            return "A discovered Wi-Fi network must provide an SSID before it can be connected.";
        }

        string securityType = ResolveDiscoveredWifiSecurityType(authentication);
        if (string.Equals(securityType, EnterpriseSecurityType, StringComparison.OrdinalIgnoreCase))
        {
            return "Enterprise Wi-Fi from the discovery list requires a provisioned profile template in this build.";
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "Foundry.Connect");
        Directory.CreateDirectory(tempRoot);

        string safeFileName = string.Concat(trimmedSsid.Select(static ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        string profilePath = Path.Combine(tempRoot, $"discovered-{safeFileName}.xml");
        await File.WriteAllTextAsync(
            profilePath,
            BuildWifiProfileXml(trimmedSsid, securityType, passphrase),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        ProcessExecutionResult addProfileResult = await ExecuteProcessAsync(
            "netsh",
            $"wlan add profile filename=\"{profilePath}\" user=all",
            cancellationToken).ConfigureAwait(false);
        if (addProfileResult.ExitCode != 0)
        {
            return $"Wi-Fi profile import failed for '{trimmedSsid}': {CollapseError(addProfileResult)}";
        }

        ProcessExecutionResult connectResult = await ExecuteProcessAsync(
            "netsh",
            $"wlan connect name=\"{trimmedSsid}\" ssid=\"{trimmedSsid}\"",
            cancellationToken).ConfigureAwait(false);
        if (connectResult.ExitCode == 0)
        {
            return $"Wi-Fi connection attempt started for '{trimmedSsid}'.";
        }

        return $"Wi-Fi connection attempt failed for '{trimmedSsid}': {CollapseError(connectResult)}";
    }

    private async Task<string> ApplyWiredDot1xProfileAsync(CancellationToken cancellationToken)
    {
        string? profilePath = ResolveAssetPath(_configuration.Dot1x.ProfileTemplatePath);
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return "Wired 802.1X is enabled, but no wired profile template was found.";
        }

        List<string> messages = [];
        string? certificatePath = ResolveAssetPath(_configuration.Dot1x.CertificatePath);
        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            messages.Add(ImportCertificate(certificatePath));
        }

        if (_configuration.Dot1x.AllowRuntimeCredentials)
        {
            messages.Add("Runtime-entered wired 802.1X credentials are not supported in this build. Use a profile template that already contains the required enterprise settings.");
        }

        ProcessExecutionResult addProfileResult = await ExecuteProcessAsync(
            "netsh",
            $"lan add profile filename=\"{profilePath}\"",
            cancellationToken).ConfigureAwait(false);
        if (addProfileResult.ExitCode != 0)
        {
            _logger.LogWarning(
                "Failed to add wired 802.1X profile. ExitCode={ExitCode}, StdOut={StdOut}, StdErr={StdErr}",
                addProfileResult.ExitCode,
                addProfileResult.StandardOutput,
                addProfileResult.StandardError);
            messages.Add($"Wired 802.1X profile import failed: {CollapseError(addProfileResult)}");
            return string.Join(" ", messages);
        }

        messages.Add("Wired 802.1X profile imported.");

        string? ethernetInterfaceName = GetEthernetInterfaceName();
        if (!string.IsNullOrWhiteSpace(ethernetInterfaceName))
        {
            ProcessExecutionResult reconnectResult = await ExecuteProcessAsync(
                "netsh",
                $"lan reconnect interface=\"{ethernetInterfaceName}\"",
                cancellationToken).ConfigureAwait(false);
            messages.Add(reconnectResult.ExitCode == 0
                ? $"Wired reconnect requested on '{ethernetInterfaceName}'."
                : $"Wired reconnect request failed: {CollapseError(reconnectResult)}");
        }

        return string.Join(" ", messages);
    }

    private async Task<string> EnsureWifiProfileAsync(CancellationToken cancellationToken)
    {
        List<string> messages = [];

        string? certificatePath = ResolveAssetPath(_configuration.Wifi.CertificatePath);
        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            messages.Add(ImportCertificate(certificatePath));
        }

        string? wifiProfilePath = await EnsureWifiProfileFileAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wifiProfilePath))
        {
            messages.Add("No Wi-Fi profile is configured for this image.");
            return string.Join(" ", messages);
        }

        ProcessExecutionResult addProfileResult = await ExecuteProcessAsync(
            "netsh",
            $"wlan add profile filename=\"{wifiProfilePath}\" user=all",
            cancellationToken).ConfigureAwait(false);

        if (addProfileResult.ExitCode != 0)
        {
            _logger.LogWarning(
                "Failed to add Wi-Fi profile. ExitCode={ExitCode}, StdOut={StdOut}, StdErr={StdErr}",
                addProfileResult.ExitCode,
                addProfileResult.StandardOutput,
                addProfileResult.StandardError);
            messages.Add($"Wi-Fi profile import failed: {CollapseError(addProfileResult)}");
        }
        else
        {
            messages.Add("Wi-Fi profile imported.");
        }

        if (_configuration.Wifi.HasEnterpriseProfile && _configuration.Wifi.AllowRuntimeCredentials)
        {
            messages.Add("Runtime-entered Wi-Fi 802.1X credentials are not supported in this build. Use a provisioned enterprise profile template.");
        }

        return string.Join(" ", messages);
    }

    private async Task<string?> EnsureWifiProfileFileAsync(CancellationToken cancellationToken)
    {
        if (_configuration.Wifi.HasEnterpriseProfile)
        {
            string? enterpriseProfilePath = ResolveAssetPath(_configuration.Wifi.EnterpriseProfileTemplatePath);
            return !string.IsNullOrWhiteSpace(enterpriseProfilePath) && File.Exists(enterpriseProfilePath)
                ? enterpriseProfilePath
                : null;
        }

        if (string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid) || string.IsNullOrWhiteSpace(_configuration.Wifi.SecurityType))
        {
            return null;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "Foundry.Connect");
        Directory.CreateDirectory(tempRoot);

        string profilePath = Path.Combine(tempRoot, "configured-wifi-profile.xml");
        await File.WriteAllTextAsync(
            profilePath,
            BuildWifiProfileXml(_configuration.Wifi.Ssid.Trim(), _configuration.Wifi.SecurityType.Trim(), _configuration.Wifi.Passphrase),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        return profilePath;
    }

    private string? ResolveWifiProfileName()
    {
        if (_configuration.Wifi.HasEnterpriseProfile)
        {
            string? profilePath = ResolveAssetPath(_configuration.Wifi.EnterpriseProfileTemplatePath);
            if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
            {
                return null;
            }

            string fileContents = File.ReadAllText(profilePath);
            const string openTag = "<name>";
            const string closeTag = "</name>";
            int startIndex = fileContents.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            int endIndex = fileContents.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0 || endIndex <= startIndex)
            {
                return null;
            }

            int contentStart = startIndex + openTag.Length;
            return fileContents[contentStart..endIndex].Trim();
        }

        if (!string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid))
        {
            return _configuration.Wifi.Ssid.Trim();
        }

        return null;
    }

    private string? GetEthernetInterfaceName()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(static adapter => adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetFx
                or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.Ethernet3Megabit)
            .Select(static adapter => adapter.Name)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private string ImportCertificate(string certificatePath)
    {
        try
        {
            X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            using X509Store rootStore = new(StoreName.Root, StoreLocation.LocalMachine);
            rootStore.Open(OpenFlags.ReadWrite);

            bool alreadyPresent = rootStore.Certificates
                .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
                .Count > 0;
            if (!alreadyPresent)
            {
                rootStore.Add(certificate);
            }

            return alreadyPresent
                ? $"Certificate '{Path.GetFileName(certificatePath)}' was already trusted."
                : $"Certificate '{Path.GetFileName(certificatePath)}' was imported into the local machine root store.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import certificate from {CertificatePath}.", certificatePath);
            return $"Certificate import failed for '{Path.GetFileName(certificatePath)}': {ex.Message}";
        }
    }

    private string? ResolveAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        string? configurationDirectoryPath = null;
        if (!string.IsNullOrWhiteSpace(_configurationService.ConfigurationPath))
        {
            configurationDirectoryPath = Path.GetDirectoryName(_configurationService.ConfigurationPath);
        }

        configurationDirectoryPath ??= AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(configurationDirectoryPath, trimmed));
    }

    private static string BuildWifiProfileXml(string ssidValue, string securityType, string? passphraseValue)
    {
        string ssid = SecurityElement.Escape(ssidValue.Trim()) ?? string.Empty;
        bool isOpen = string.Equals(securityType, "Open", StringComparison.OrdinalIgnoreCase);
        bool isPersonal = string.Equals(securityType, "WPA2-Personal", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(securityType, "WPA2/WPA3-Personal", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(securityType, "WPA3-Personal", StringComparison.OrdinalIgnoreCase);

        if (isOpen)
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <name>{{ssid}}</name>
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>open</authentication>
        <encryption>none</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
    </security>
  </MSM>
</WLANProfile>
""";
        }

        if (isPersonal)
        {
            string passphrase = SecurityElement.Escape(passphraseValue?.Trim() ?? string.Empty) ?? string.Empty;
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <name>{{ssid}}</name>
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>WPA2PSK</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>{{passphrase}}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
</WLANProfile>
""";
        }

        throw new InvalidOperationException($"Unsupported Wi-Fi security type '{securityType}'.");
    }

    private static string ResolveDiscoveredWifiSecurityType(string authentication)
    {
        if (authentication.Contains("open", StringComparison.OrdinalIgnoreCase))
        {
            return "Open";
        }

        if (authentication.Contains("personal", StringComparison.OrdinalIgnoreCase) ||
            authentication.Contains("psk", StringComparison.OrdinalIgnoreCase))
        {
            return "WPA2-Personal";
        }

        return EnterpriseSecurityType;
    }

    private static string CollapseError(ProcessExecutionResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Exit code {result.ExitCode}.";
        }

        return message.Replace(Environment.NewLine, " ").Trim();
    }

    private async Task<ProcessExecutionResult> ExecuteProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessExecutionResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Process execution failed for {FileName} {Arguments}.", fileName, arguments);
            return new ProcessExecutionResult(-1, string.Empty, ex.Message);
        }
    }

    private readonly record struct ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);
}
