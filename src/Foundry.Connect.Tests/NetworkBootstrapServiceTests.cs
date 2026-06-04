using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class NetworkBootstrapServiceTests
{
    [Fact]
    public async Task ApplyProvisionedSettingsAsync_WhenProvisionedWifiHasNoWinPeAdapter_StillCapturesProfileForRoaming()
    {
        var configuration = new FoundryConnectConfiguration
        {
            Capabilities = new NetworkCapabilitiesOptions
            {
                WifiProvisioned = true
            },
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Foundry",
                SecurityType = "Open"
            }
        };
        var roamingService = new CapturingNetworkProfileRoamingService();
        var service = new NetworkBootstrapService(
            configuration,
            new FakeConnectConfigurationService(configuration),
            roamingService,
            NullLogger<NetworkBootstrapService>.Instance,
            getWifiInterfaceIds: static () => []);

        string result = await service.ApplyProvisionedSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Contains("No wireless adapter is available", result, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(roamingService.WifiCaptureRequest);
        Assert.Equal(NetworkProfileRoamingProfileSource.ProvisionedWifi, roamingService.WifiCaptureRequest.Source);
        Assert.True(roamingService.ProfileExistedDuringCapture);
    }

    private sealed class CapturingNetworkProfileRoamingService : INetworkProfileRoamingService
    {
        public NetworkProfileRoamingCaptureRequest? WifiCaptureRequest { get; private set; }

        public bool ProfileExistedDuringCapture { get; private set; }

        public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
        {
            WifiCaptureRequest = request;
            ProfileExistedDuringCapture = File.Exists(request.ProfilePath);
            return Task.CompletedTask;
        }

        public Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnectConfigurationService(FoundryConnectConfiguration configuration) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => false;

        public FoundryConnectConfiguration Load() => configuration;
    }
}
