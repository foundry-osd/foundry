using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

public sealed class NetworkBootstrapService : INetworkBootstrapService
{
    private const string OpenSecurityType = "Open";
    private const string OweSecurityType = "OWE";
    private const string EnterpriseSecurityType = "Enterprise";
    private const string WifiSecurityPersonal = "WPA2/WPA3-Personal";
    private const string WifiSecurityLegacyWpa2Personal = "WPA2-Personal";
    private const string WifiSecurityWpa3Personal = "WPA3-Personal";
    private const string WifiSecurityLegacyPersonal = "Personal";
    private static readonly TimeSpan WifiConnectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WifiConnectionPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WifiProfileImportRetryDelay = TimeSpan.FromSeconds(2);
    private const int WinPeWifiProfileImportRetryCount = 3;

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

        IReadOnlyList<Guid> wirelessInterfaceIds = NativeWifiApi.GetInterfaceIds();
        if (wirelessInterfaceIds.Count == 0)
        {
            return $"{ensureMessage} No wireless adapter is available to connect the provisioned Wi-Fi profile.";
        }

        string arguments = $"wlan connect name=\"{EscapeNetshArgument(profileName)}\"";

        ProcessExecutionResult result = await ExecuteProcessAsync("netsh", arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _logger.LogWarning(
                "Wi-Fi connection request failed. ExitCode={ExitCode}, StdOut={StdOut}, StdErr={StdErr}",
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
            return $"{ensureMessage} Wi-Fi connection request failed: {CollapseError(result)}";
        }

        string expectedSsid = string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid)
            ? profileName
            : _configuration.Wifi.Ssid.Trim();
        WifiConnectionAttemptResult attemptResult = await WaitForWifiConnectionAsync(
            wirelessInterfaceIds,
            expectedSsid,
            cancellationToken).ConfigureAwait(false);

        return attemptResult.IsConnected
            ? $"{ensureMessage} Wi-Fi connected to '{expectedSsid}'."
            : $"{ensureMessage} Wi-Fi connection failed: {attemptResult.FailureMessage}";
    }

    public async Task<string> ConnectWifiNetworkAsync(string ssid, string? ssidHex, string authentication, string? passphrase, CancellationToken cancellationToken)
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

        string? profilePath = null;
        try
        {
            profilePath = await WriteTemporaryWifiProfileAsync(
                BuildWifiProfileXml(trimmedSsid, securityType, passphrase, ssidHex),
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<Guid> wirelessInterfaceIds = NativeWifiApi.GetInterfaceIds();
            if (wirelessInterfaceIds.Count == 0)
            {
                return "No wireless adapter is available to connect the selected Wi-Fi network.";
            }

            ProcessExecutionResult addProfileResult = await ImportWifiProfileAsync(profilePath, cancellationToken).ConfigureAwait(false);
            if (addProfileResult.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Failed to import discovered Wi-Fi profile. Ssid={Ssid}, ExitCode={ExitCode}",
                    trimmedSsid,
                    addProfileResult.ExitCode);
                return $"Wi-Fi profile import failed for '{trimmedSsid}': {CollapseError(addProfileResult)}";
            }

            ProcessExecutionResult connectResult = await ExecuteProcessAsync(
                "netsh",
                $"wlan connect name=\"{EscapeNetshArgument(trimmedSsid)}\"",
                cancellationToken).ConfigureAwait(false);
            if (connectResult.ExitCode != 0)
            {
                return $"Wi-Fi connection request failed for '{trimmedSsid}': {CollapseError(connectResult)}";
            }

            WifiConnectionAttemptResult attemptResult = await WaitForWifiConnectionAsync(
                wirelessInterfaceIds,
                trimmedSsid,
                cancellationToken).ConfigureAwait(false);

            return attemptResult.IsConnected
                ? $"Wi-Fi connected to '{trimmedSsid}'."
                : $"Wi-Fi connection failed for '{trimmedSsid}': {attemptResult.FailureMessage}";
        }
        finally
        {
            DeleteTemporaryProfile(profilePath);
        }
    }

    public async Task<string> DisconnectWifiAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> wirelessInterfaceIds = NativeWifiApi.GetInterfaceIds();
        if (wirelessInterfaceIds.Count == 0)
        {
            return "No wireless adapter is available to disconnect.";
        }

        string? connectedSsid = NativeWifiApi.GetConnectedSsid();
        if (string.IsNullOrWhiteSpace(connectedSsid))
        {
            return "Wi-Fi is already disconnected.";
        }

        ProcessExecutionResult disconnectResult = await ExecuteProcessAsync(
            "netsh",
            "wlan disconnect",
            cancellationToken).ConfigureAwait(false);
        if (disconnectResult.ExitCode != 0)
        {
            return $"Wi-Fi disconnect request failed: {CollapseError(disconnectResult)}";
        }

        WifiDisconnectAttemptResult attemptResult = await WaitForWifiDisconnectionAsync(
            wirelessInterfaceIds,
            connectedSsid,
            cancellationToken).ConfigureAwait(false);

        return attemptResult.IsDisconnected
            ? $"Wi-Fi disconnected from '{connectedSsid}'."
            : $"Wi-Fi disconnect failed: {attemptResult.FailureMessage}";
    }

    private async Task<string> ApplyWiredDot1xProfileAsync(CancellationToken cancellationToken)
    {
        string? profilePath = ProvisionedWifiProfileResolver.ResolveAssetPath(
            _configuration.Dot1x.ProfileTemplatePath,
            _configurationService.ConfigurationPath);
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return "Wired 802.1X is enabled, but no wired profile template was found.";
        }

        List<string> messages = [];
        string? certificatePath = ProvisionedWifiProfileResolver.ResolveAssetPath(
            _configuration.Dot1x.CertificatePath,
            _configurationService.ConfigurationPath);
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

        string? certificatePath = ProvisionedWifiProfileResolver.ResolveAssetPath(
            _configuration.Wifi.CertificatePath,
            _configurationService.ConfigurationPath);
        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            messages.Add(ImportCertificate(certificatePath));
        }

        if (NativeWifiApi.GetInterfaceIds().Count == 0)
        {
            return string.Join(" ", messages);
        }

        string? wifiProfilePath = await EnsureWifiProfileFileAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wifiProfilePath))
        {
            messages.Add("No Wi-Fi profile is configured for this image.");
            return string.Join(" ", messages);
        }

        try
        {
            ProcessExecutionResult addProfileResult = await ImportWifiProfileAsync(wifiProfilePath, cancellationToken).ConfigureAwait(false);

            if (addProfileResult.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Failed to add provisioned Wi-Fi profile. ExitCode={ExitCode}",
                    addProfileResult.ExitCode);
                messages.Add($"Wi-Fi profile import failed: {CollapseError(addProfileResult)}");
            }
            else
            {
                messages.Add("Wi-Fi profile imported.");
            }
        }
        finally
        {
            if (!_configuration.Wifi.HasEnterpriseProfile)
            {
                DeleteTemporaryProfile(wifiProfilePath);
            }
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
            string? enterpriseProfilePath = ProvisionedWifiProfileResolver.ResolveAssetPath(
                _configuration.Wifi.EnterpriseProfileTemplatePath,
                _configurationService.ConfigurationPath);
            return !string.IsNullOrWhiteSpace(enterpriseProfilePath) && File.Exists(enterpriseProfilePath)
                ? enterpriseProfilePath
                : null;
        }

        if (string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid) || string.IsNullOrWhiteSpace(_configuration.Wifi.SecurityType))
        {
            return null;
        }

        return await WriteTemporaryWifiProfileAsync(
            BuildWifiProfileXml(_configuration.Wifi.Ssid.Trim(), _configuration.Wifi.SecurityType.Trim(), _configuration.Wifi.Passphrase),
            cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveWifiProfileName()
    {
        return ProvisionedWifiProfileResolver.ResolveProfileName(
            _configuration.Wifi,
            _configurationService.ConfigurationPath);
    }

    private async Task<ProcessExecutionResult> ImportWifiProfileAsync(
        string profilePath,
        CancellationToken cancellationToken)
    {
        int maxAttempts = ConnectWorkspacePaths.IsWinPeRuntime()
            ? WinPeWifiProfileImportRetryCount
            : 1;

        ProcessExecutionResult lastResult = default;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastResult = await ExecuteProcessAsync(
                "netsh",
                $"wlan add profile filename=\"{profilePath}\"",
                cancellationToken).ConfigureAwait(false);

            if (lastResult.ExitCode == 0)
            {
                return lastResult;
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            _logger.LogInformation(
                "Wi-Fi profile import attempt {Attempt} failed in WinPE. Retrying in {DelaySeconds}s. ExitCode={ExitCode}",
                attempt,
                WifiProfileImportRetryDelay.TotalSeconds,
                lastResult.ExitCode);

            await Task.Delay(WifiProfileImportRetryDelay, cancellationToken).ConfigureAwait(false);
        }

        return lastResult;
    }

    private async Task<string> WriteTemporaryWifiProfileAsync(
        string profileXml,
        CancellationToken cancellationToken)
    {
        string profilePath = Path.Combine(ResolveTemporaryProfileRoot(), $"wifi-profile-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(
            profilePath,
            profileXml,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        return profilePath;
    }

    private void DeleteTemporaryProfile(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return;
        }

        try
        {
            if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete a temporary Wi-Fi profile file.");
        }
    }

    private string ResolveTemporaryProfileRoot()
    {
        foreach (string candidateDirectory in ConnectWorkspacePaths.EnumerateTemporaryDirectories("Foundry.Connect"))
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidateDirectory);
                return candidateDirectory;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to create temporary Wi-Fi profile directory at {CandidateDirectory}.", candidateDirectory);
            }
        }

        throw new InvalidOperationException("No writable temporary directory is available for Wi-Fi profile generation.");
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

    private static string EscapeNetshArgument(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private static bool IsConnectionInProgress(NativeWifiApi.WlanInterfaceState state)
    {
        return state is NativeWifiApi.WlanInterfaceState.Associating
            or NativeWifiApi.WlanInterfaceState.Authenticating
            or NativeWifiApi.WlanInterfaceState.Discovering
            or NativeWifiApi.WlanInterfaceState.Disconnecting;
    }

    private static async Task<WifiConnectionAttemptResult> WaitForWifiConnectionAsync(
        IReadOnlyList<Guid> interfaceIds,
        string expectedSsid,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + WifiConnectionTimeout;
        bool sawConnectionTransition = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (Guid interfaceId in interfaceIds)
            {
                NativeWifiApi.WifiInterfaceConnectionInfo? connectionInfo = NativeWifiApi.GetInterfaceConnectionInfo(interfaceId);
                if (connectionInfo is null)
                {
                    continue;
                }

                if (connectionInfo.State == NativeWifiApi.WlanInterfaceState.Connected &&
                    string.Equals(connectionInfo.CurrentSsid, expectedSsid, StringComparison.Ordinal))
                {
                    return WifiConnectionAttemptResult.Success();
                }

                if (IsConnectionInProgress(connectionInfo.State))
                {
                    sawConnectionTransition = true;
                }
            }

            await Task.Delay(WifiConnectionPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return sawConnectionTransition
            ? WifiConnectionAttemptResult.Failure($"Windows started the Wi-Fi connection workflow, but '{expectedSsid}' did not reach the connected state within {WifiConnectionTimeout.TotalSeconds:0} seconds.")
            : WifiConnectionAttemptResult.Failure($"Windows accepted the request, but the wireless interface never transitioned into an active connection attempt.");
    }

    private static async Task<WifiDisconnectAttemptResult> WaitForWifiDisconnectionAsync(
        IReadOnlyList<Guid> interfaceIds,
        string disconnectedSsid,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + WifiConnectionTimeout;
        bool sawDisconnectTransition = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isStillConnectedToTarget = false;

            foreach (Guid interfaceId in interfaceIds)
            {
                NativeWifiApi.WifiInterfaceConnectionInfo? connectionInfo = NativeWifiApi.GetInterfaceConnectionInfo(interfaceId);
                if (connectionInfo is null)
                {
                    continue;
                }

                if (connectionInfo.State == NativeWifiApi.WlanInterfaceState.Disconnecting)
                {
                    sawDisconnectTransition = true;
                }

                if (connectionInfo.State == NativeWifiApi.WlanInterfaceState.Connected &&
                    string.Equals(connectionInfo.CurrentSsid, disconnectedSsid, StringComparison.Ordinal))
                {
                    isStillConnectedToTarget = true;
                }
            }

            if (!isStillConnectedToTarget)
            {
                return WifiDisconnectAttemptResult.Success();
            }

            await Task.Delay(WifiConnectionPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return sawDisconnectTransition
            ? WifiDisconnectAttemptResult.Failure($"Windows started the Wi-Fi disconnect workflow, but '{disconnectedSsid}' remained connected after {WifiConnectionTimeout.TotalSeconds:0} seconds.")
            : WifiDisconnectAttemptResult.Failure($"Windows accepted the request, but '{disconnectedSsid}' did not transition away from the connected state.");
    }

    private static string BuildWifiProfileXml(string ssidValue, string securityType, string? passphraseValue, string? ssidHexOverride = null)
    {
        string ssid = SecurityElement.Escape(ssidValue.Trim()) ?? string.Empty;
        string ssidHex = string.IsNullOrWhiteSpace(ssidHexOverride)
            ? ConvertSsidToHex(ssidValue.Trim())
            : ssidHexOverride.Trim();
        bool isOpen = string.Equals(securityType, OpenSecurityType, StringComparison.OrdinalIgnoreCase);
        bool isOwe = string.Equals(securityType, OweSecurityType, StringComparison.OrdinalIgnoreCase);
        bool isPersonal = IsPersonalSecurityType(securityType);

        if (isOpen)
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
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
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        if (isPersonal)
        {
            string passphrase = SecurityElement.Escape(passphraseValue?.Trim() ?? string.Empty) ?? string.Empty;
            string authentication = ResolvePersonalAuthentication(securityType);
            string transitionMode = string.Equals(securityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase)
                ? """
        <transitionMode xmlns="http://www.microsoft.com/networking/WLAN/profile/v4">true</transitionMode>
"""
                : string.Empty;
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      <name>{{ssid}}</name>
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>{{authentication}}</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
{{transitionMode}}      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>{{passphrase}}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        if (isOwe)
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      <name>{{ssid}}</name>
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>OWE</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
    </security>
  </MSM>
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        throw new InvalidOperationException($"Unsupported Wi-Fi security type '{securityType}'.");
    }

    private static string ResolveDiscoveredWifiSecurityType(string authentication)
    {
        if (authentication.Contains("enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return EnterpriseSecurityType;
        }

        if (authentication.Contains("open", StringComparison.OrdinalIgnoreCase))
        {
            return OpenSecurityType;
        }

        if (authentication.Contains("owe", StringComparison.OrdinalIgnoreCase))
        {
            return OweSecurityType;
        }

        if (authentication.Contains("sae", StringComparison.OrdinalIgnoreCase) ||
            authentication.Contains("wpa3", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityWpa3Personal;
        }

        if (authentication.Contains("personal", StringComparison.OrdinalIgnoreCase) ||
            authentication.Contains("psk", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityLegacyWpa2Personal;
        }

        return EnterpriseSecurityType;
    }

    private static bool IsPersonalSecurityType(string securityType)
    {
        return string.Equals(securityType, WifiSecurityLegacyWpa2Personal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityWpa3Personal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityLegacyPersonal, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePersonalAuthentication(string securityType)
    {
        if (string.Equals(securityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, WifiSecurityWpa3Personal, StringComparison.OrdinalIgnoreCase))
        {
            return "WPA3SAE";
        }

        return "WPA2PSK";
    }

    private static string ConvertSsidToHex(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        StringBuilder builder = new(bytes.Length * 2);

        foreach (byte currentByte in bytes)
        {
            builder.Append(currentByte.ToString("X2"));
        }

        return builder.ToString();
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

    private sealed record WifiConnectionAttemptResult(bool IsConnected, string? FailureMessage)
    {
        public static WifiConnectionAttemptResult Success()
        {
            return new WifiConnectionAttemptResult(true, null);
        }

        public static WifiConnectionAttemptResult Failure(string message)
        {
            return new WifiConnectionAttemptResult(false, message);
        }
    }

    private sealed record WifiDisconnectAttemptResult(bool IsDisconnected, string? FailureMessage)
    {
        public static WifiDisconnectAttemptResult Success()
        {
            return new WifiDisconnectAttemptResult(true, null);
        }

        public static WifiDisconnectAttemptResult Failure(string message)
        {
            return new WifiDisconnectAttemptResult(false, message);
        }
    }

    private readonly record struct ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);
}
