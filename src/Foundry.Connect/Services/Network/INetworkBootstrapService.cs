namespace Foundry.Connect.Services.Network;

/// <summary>
/// Applies provisioned network settings and controls Wi-Fi connection state.
/// </summary>
public interface INetworkBootstrapService
{
    /// <summary>
    /// Imports provisioned wired and Wi-Fi profiles from the runtime configuration.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel network commands.</param>
    /// <returns>A user-facing status message.</returns>
    Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Connects the Wi-Fi profile supplied by the provisioned configuration.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel network commands.</param>
    /// <returns>A user-facing status message.</returns>
    Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Connects a discovered personal or open Wi-Fi network.
    /// </summary>
    /// <param name="ssid">The discovered SSID.</param>
    /// <param name="ssidHex">The optional SSID hex value returned by WLAN APIs.</param>
    /// <param name="authentication">The discovered authentication description.</param>
    /// <param name="passphrase">The optional personal Wi-Fi passphrase.</param>
    /// <param name="cancellationToken">A token used to cancel network commands.</param>
    /// <returns>A user-facing status message.</returns>
    Task<string> ConnectWifiNetworkAsync(string ssid, string? ssidHex, string authentication, string? passphrase, CancellationToken cancellationToken);

    /// <summary>
    /// Disconnects the currently connected Wi-Fi network.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel network commands.</param>
    /// <returns>A user-facing status message.</returns>
    Task<string> DisconnectWifiAsync(CancellationToken cancellationToken);
}
