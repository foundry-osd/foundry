// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Xml;
using System.Xml.Linq;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Services.Network;
using Foundry.Core.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Writes the network profiles selected by <see cref="RoamingSelectionPolicy"/> to the roaming artifact at successful
/// exit (D5).
/// </summary>
public interface IRoamingCapture
{
    /// <summary>
    /// Captures the selected Wi-Fi and wired 802.1X profiles. Never throws; a failure is a Warning and never changes
    /// the exit outcome (spec 5.5).
    /// </summary>
    /// <param name="state">Network state at exit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the capture ends.</returns>
    Task CaptureAsync(NetworkState state, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="IRoamingCapture"/>.
/// </summary>
/// <remarks>
/// A provisioned enterprise Wi-Fi profile is roamed from the media template with its certificate. Every other Wi-Fi
/// profile is read back from WLAN with its key in plain text; a profile that cannot be read, or whose key is still
/// protected, is skipped with one Warning. The SSID, the profile name and the profile XML are never logged (the remote
/// Logs stream mirrors every property).
/// </remarks>
public sealed class RoamingCapture : IRoamingCapture
{
    private const string WifiProfileKind = "Wifi";
    private const string WiredDot1xProfileKind = "WiredDot1x";

    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly string? _configurationPath;
    private readonly IWifiConnectionHistory _history;
    private readonly IWlanClient _wlan;
    private readonly INetworkProfileRoamingService _roaming;
    private readonly ILogger<RoamingCapture> _logger;

    /// <summary>
    /// Creates the capture.
    /// </summary>
    /// <param name="configuration">Loaded configuration.</param>
    /// <param name="configurationService">Configuration service; its path resolves media asset paths.</param>
    /// <param name="history">Successful Wi-Fi connections of this session.</param>
    /// <param name="wlan">WLAN client used to read profiles back.</param>
    /// <param name="roaming">Roaming artifact writer.</param>
    /// <param name="logger">Logger.</param>
    public RoamingCapture(
        FoundryConnectConfigurationDocument configuration,
        IConnectConfigurationService configurationService,
        IWifiConnectionHistory history,
        IWlanClient wlan,
        INetworkProfileRoamingService roaming,
        ILogger<RoamingCapture> logger)
        : this(configuration, configurationService.ConfigurationPath, history, wlan, roaming, logger)
    {
    }

    internal RoamingCapture(
        FoundryConnectConfigurationDocument configuration,
        string? configurationPath,
        IWifiConnectionHistory history,
        IWlanClient wlan,
        INetworkProfileRoamingService roaming,
        ILogger<RoamingCapture> logger)
    {
        _configuration = configuration;
        _configurationPath = configurationPath;
        _history = history;
        _wlan = wlan;
        _roaming = roaming;
        _logger = logger;
    }

    private enum KeyProtection
    {
        Readable,
        Protected,
        Unreadable
    }

    /// <inheritdoc />
    public async Task CaptureAsync(NetworkState state, CancellationToken cancellationToken)
    {
        RoamingSelection selection = RoamingSelectionPolicy.Select(
            state,
            _history.SuccessfulWifiConnections,
            _configuration.Network.ProfileRoaming,
            _configuration.Dot1x.IsEnabled);
        _logger.LogDebug(
            "Network profile roaming selection. WifiProfileCount={WifiProfileCount}, CaptureWiredDot1x={CaptureWiredDot1x}",
            selection.WifiProfiles.Count,
            selection.CaptureWiredDot1x);

        if (selection.WifiProfiles.Count > 0)
        {
            await RunCaptureAsync(WifiProfileKind, () => CaptureWifiAsync(selection.WifiProfiles, cancellationToken)).ConfigureAwait(false);
        }

        if (selection.CaptureWiredDot1x)
        {
            await RunCaptureAsync(WiredDot1xProfileKind, () => _roaming.CaptureWiredDot1xProfileAsync(CreateWiredRequest(), cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task RunCaptureAsync(string profileKind, Func<Task> capture)
    {
        try
        {
            await capture().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The exception is not attached: its message could name a profile file.
            _logger.LogWarning(
                "Network profile roaming capture failed. ProfileKind={ProfileKind}, FailureType={FailureType}",
                profileKind,
                exception.GetType().Name);
        }
    }

    private Task CaptureWifiAsync(IReadOnlyList<SuccessfulWifiConnection> connections, CancellationToken cancellationToken)
    {
        List<NetworkProfileRoamingCaptureRequest> requests = [];
        foreach (SuccessfulWifiConnection connection in connections)
        {
            if (CreateWifiRequest(connection) is NetworkProfileRoamingCaptureRequest request)
            {
                requests.Add(request);
            }
        }

        return requests.Count == 0 ? Task.CompletedTask : _roaming.CaptureWifiProfilesAsync(requests, cancellationToken);
    }

    private NetworkProfileRoamingCaptureRequest? CreateWifiRequest(SuccessfulWifiConnection connection)
    {
        ConnectWifiSettings wifi = _configuration.Wifi;
        bool isProvisioned = connection.Source == WifiConnectionSource.Provisioned;
        NetworkProfileRoamingProfileSource source = isProvisioned
            ? NetworkProfileRoamingProfileSource.ProvisionedWifi
            : NetworkProfileRoamingProfileSource.ManualWifi;

        if (isProvisioned && wifi.HasEnterpriseProfile)
        {
            return new NetworkProfileRoamingCaptureRequest(
                ResolveAssetPath(wifi.EnterpriseProfileTemplatePath),
                source,
                ResolveConnectivityExpectation(wifi.EnterpriseAuthenticationMode),
                ResolveExistingAssetPaths(wifi.CertificatePath),
                wifi.CertificatePfxPasswordSecret);
        }

        string? profileXml = _wlan.GetProfileXml(connection.InterfaceId, connection.ProfileName);
        switch (profileXml is null ? KeyProtection.Unreadable : ReadKeyProtection(profileXml))
        {
            case KeyProtection.Unreadable:
                _logger.LogWarning("Wi-Fi roaming capture skipped because the profile could not be read. Source={Source}", connection.Source);
                return null;
            case KeyProtection.Protected:
                _logger.LogWarning("Wi-Fi roaming capture skipped because the key is protected. Source={Source}", connection.Source);
                return null;
            default:
                return new NetworkProfileRoamingCaptureRequest(
                    null,
                    source,
                    NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable,
                    ProfileXml: profileXml);
        }
    }

    private NetworkProfileRoamingCaptureRequest CreateWiredRequest()
    {
        ConnectDot1xSettings dot1x = _configuration.Dot1x;
        return new NetworkProfileRoamingCaptureRequest(
            ResolveAssetPath(dot1x.ProfileTemplatePath),
            NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
            ResolveConnectivityExpectation(dot1x.AuthenticationMode),
            ResolveExistingAssetPaths(dot1x.CertificatePath),
            dot1x.CertificatePfxPasswordSecret);
    }

    private string? ResolveAssetPath(string? value) => ProvisionedWifiProfileResolver.ResolveAssetPath(value, _configurationPath);

    private IReadOnlyList<string> ResolveExistingAssetPaths(string? value) =>
        ResolveAssetPath(value) is string path && File.Exists(path) ? [path] : [];

    // A machine-only profile authenticates before sign-in; any other mode needs a user and is import-only before OOBE.
    private static NetworkProfileRoamingConnectivityExpectation ResolveConnectivityExpectation(NetworkAuthenticationMode mode) =>
        mode == NetworkAuthenticationMode.MachineOnly
            ? NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential
            : NetworkProfileRoamingConnectivityExpectation.ImportOnly;

    // WlanGetProfile returns the key in plain text only to an administrator; otherwise sharedKey/protected stays true
    // and the roamed profile could not be imported elsewhere. A profile without a shared key (open, OWE) is readable.
    private static KeyProtection ReadKeyProtection(string profileXml)
    {
        XDocument document;
        try
        {
            XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using StringReader text = new(profileXml);
            using XmlReader reader = XmlReader.Create(text, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return KeyProtection.Unreadable;
        }

        XElement? protectedElement = document.Descendants()
            .Where(static element => element.Name.LocalName == "sharedKey")
            .Elements()
            .FirstOrDefault(static element => element.Name.LocalName == "protected");
        return string.Equals(protectedElement?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            ? KeyProtection.Protected
            : KeyProtection.Readable;
    }
}
