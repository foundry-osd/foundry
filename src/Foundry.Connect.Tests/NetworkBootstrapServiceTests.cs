// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    [Fact]
    public async Task ApplyProvisionedSettingsAsync_WhenCancelledBeforeNativeCommand_ThrowsCancellation()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("wired.xml", "<LANProfile />");
        var configuration = new FoundryConnectConfiguration
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplatePath = profilePath
            }
        };
        var roamingService = new CapturingNetworkProfileRoamingService();
        var service = new NetworkBootstrapService(
            configuration,
            new FakeConnectConfigurationService(configuration),
            roamingService,
            NullLogger<NetworkBootstrapService>.Instance,
            getWifiInterfaceIds: static () => []);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ApplyProvisionedSettingsAsync(cancellationTokenSource.Token));
        Assert.Null(roamingService.WiredCaptureRequest);
    }

    private sealed class CapturingNetworkProfileRoamingService : INetworkProfileRoamingService
    {
        public NetworkProfileRoamingCaptureRequest? WifiCaptureRequest { get; private set; }

        public NetworkProfileRoamingCaptureRequest? WiredCaptureRequest { get; private set; }

        public bool ProfileExistedDuringCapture { get; private set; }

        public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
        {
            WifiCaptureRequest = request;
            ProfileExistedDuringCapture = File.Exists(request.ProfilePath);
            return Task.CompletedTask;
        }

        public Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
        {
            WiredCaptureRequest = request;
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string fileName, string contents)
        {
            string path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
