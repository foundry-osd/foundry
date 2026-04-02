namespace Foundry.Connect.Services.Network;

public interface INetworkBootstrapService
{
    Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken);

    Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken);

    Task<string> ConnectWifiNetworkAsync(string ssid, string authentication, string? passphrase, CancellationToken cancellationToken);
}
