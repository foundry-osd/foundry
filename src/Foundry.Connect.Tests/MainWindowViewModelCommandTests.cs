// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelCommandTests
{
    [Fact]
    public async Task ProvisionedWifiCommands_ReflectConnectionAndActionState()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(connectedWifiSsid: "Foundry")));

        await context.ViewModel.InitializeAsync();

        Assert.False(context.ViewModel.CanConnectConfiguredWifi);
        Assert.True(context.ViewModel.CanDisconnectConfiguredWifi);
        Assert.False(context.ViewModel.ConnectConfiguredWifiCommand.CanExecute(null));
        Assert.True(context.ViewModel.DisconnectConfiguredWifiCommand.CanExecute(null));
        context.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Open", false, true)]
    [InlineData("OWE", false, true)]
    [InlineData("WPA2-Personal", true, false)]
    [InlineData("WPA2-Enterprise", false, false)]
    public async Task SelectedWifiCommand_RequiresSupportedSecurityAndPassphrase(
        string authentication,
        bool requiresPassphrase,
        bool expectedWithoutPassphrase)
    {
        WifiNetworkSummary[] networks =
        [
            new WifiNetworkSummary
            {
                Ssid = "Guest",
                Authentication = authentication,
                Encryption = "AES",
                SignalStrengthPercent = 80
            }
        ];
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(wifiNetworks: networks)));
        await context.ViewModel.InitializeAsync();
        context.ViewModel.SelectedWifiNetwork = Assert.Single(context.ViewModel.WifiNetworks);

        Assert.Equal(expectedWithoutPassphrase, context.ViewModel.CanConnectSelectedWifi);

        context.ViewModel.SelectedWifiPassphrase = "correct horse battery staple";
        Assert.Equal(requiresPassphrase || expectedWithoutPassphrase, context.ViewModel.CanConnectSelectedWifi);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task ShowAboutCommand_DelegatesToShellService()
    {
        var context = new MainWindowViewModelTestContext();

        context.ViewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, context.ShellService.ShowAboutCalls);
        context.ViewModel.Dispose();
    }
}
