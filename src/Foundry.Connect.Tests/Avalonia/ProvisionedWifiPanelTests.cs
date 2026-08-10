// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ProvisionedWifiPanelTests
{
    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    public void Presentation_KeepsTheRelevantActionVisibleWhileItRuns(
        bool isConnected,
        bool isActionInProgress,
        bool showConnect,
        bool showDisconnect)
    {
        var presentation = new ProvisionedWifiPresentation(
            IsConfigured: true,
            ShowDetails: true,
            IsConnected: isConnected,
            IsActionInProgress: isActionInProgress,
            ProfileName: "Foundry",
            Authentication: "WPA2-Personal",
            Source: "Boot image",
            Status: "Ready",
            Placeholder: string.Empty,
            Feedback: string.Empty);

        Assert.Equal(showConnect, presentation.ShowConnectAction);
        Assert.Equal(showDisconnect, presentation.ShowDisconnectAction);
    }

    [AvaloniaFact]
    public async Task Panel_WhenPersonalProfileIsAvailable_ShowsProfileDetailsAndConnectAction()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot()));
        await context.ViewModel.InitializeAsync();
        var panel = Show(context);

        Assert.Equal("Foundry", Find<TextBlock>(panel, "ProvisionedProfileName").Text);
        Assert.Equal(
            context.ViewModel.ProvisionedWifi.Authentication,
            Find<TextBlock>(panel, "ProvisionedAuthentication").Text);
        Assert.True(Find<Button>(panel, "ConnectProvisionedWifiButton").IsVisible);
        Assert.False(Find<Button>(panel, "DisconnectProvisionedWifiButton").IsVisible);

        Close(panel, context);
    }

    [AvaloniaFact]
    public async Task Panel_WhenProfileIsConnected_ShowsDisconnectAction()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(connectedWifiSsid: "Foundry")));
        await context.ViewModel.InitializeAsync();
        var panel = Show(context);

        Assert.False(Find<Button>(panel, "ConnectProvisionedWifiButton").IsVisible);
        Assert.True(Find<Button>(panel, "DisconnectProvisionedWifiButton").IsVisible);

        Close(panel, context);
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Panel_WhenWifiIsUnavailable_ShowsInlineReason(bool runtimeAvailable, bool hasAdapter)
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(
                    wifiRuntimeAvailable: runtimeAvailable,
                    hasWirelessAdapter: hasAdapter)));
        await context.ViewModel.InitializeAsync();
        var panel = Show(context);

        Assert.True(Find<TextBlock>(panel, "ProvisionedWifiPlaceholder").IsVisible);
        Assert.False(Find<Grid>(panel, "ProvisionedWifiDetails").IsVisible);

        Close(panel, context);
    }

    [AvaloniaFact]
    public async Task Panel_WhenNoProfileExists_ShowsEmptyState()
    {
        var context = new MainWindowViewModelTestContext(
            configuration: MainWindowViewModelTestContext.CreateConfiguration(wifiEnabled: false));
        await context.ViewModel.InitializeAsync();
        var panel = Show(context);

        Assert.True(Find<TextBlock>(panel, "ProvisionedWifiPlaceholder").IsVisible);
        Assert.Equal(
            context.ViewModel.ProvisionedWifi.Placeholder,
            Find<TextBlock>(panel, "ProvisionedWifiPlaceholder").Text);

        Close(panel, context);
    }

    [AvaloniaFact]
    public async Task Panel_WhenEnterpriseProfileFailsToImport_ShowsFeedbackWithoutCredentialInput()
    {
        var configuration = new FoundryConnectConfiguration
        {
            Capabilities = new NetworkCapabilitiesOptions { WifiProvisioned = true },
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplatePath = "missing.xml",
                RequiresCertificate = true
            }
        };
        var context = new MainWindowViewModelTestContext(configuration: configuration);
        context.BootstrapService.ApplyResult = "Profile import failed.";
        await context.ViewModel.InitializeAsync();
        var panel = Show(context);

        Assert.True(Find<TextBlock>(panel, "ProvisionedWifiFeedback").IsVisible);
        Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>());

        Close(panel, context);
    }

    private static ProvisionedWifiPanel Show(MainWindowViewModelTestContext context)
    {
        var panel = new ProvisionedWifiPanel { DataContext = context.ViewModel };
        var window = new Window { Content = panel };
        window.Show();
        return panel;
    }

    private static void Close(Control panel, MainWindowViewModelTestContext context)
    {
        (TopLevel.GetTopLevel(panel) as Window)?.Close();
        context.ViewModel.Dispose();
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
