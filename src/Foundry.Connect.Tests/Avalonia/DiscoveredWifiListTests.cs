// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foundry.Connect.Controls;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class DiscoveredWifiListTests
{
    [AvaloniaFact]
    public async Task Selection_DrivesPersonalOpenEnterpriseAndConnectedActions()
    {
        var context = CreateContext(CreateNetworks(), connectedWifiSsid: "Connected");
        await context.ViewModel.InitializeAsync();
        var view = Show(context);
        ListBox list = Find<ListBox>(view, "WifiNetworksList");

        list.SelectedItem = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Personal");
        DiscoveredWifiRow personalRow = FindRow(view, "Personal");
        Assert.True(personalRow.FindControl<Grid>("ConnectActions")!.IsVisible);
        var passphraseEditor = personalRow.FindControl<WifiPassphraseEditor>("PassphraseEditor")!;
        Assert.True(passphraseEditor.IsVisible);
        passphraseEditor.FindControl<Button>("RevealButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(passphraseEditor.IsRevealed);

        list.SelectedItem = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Open");
        Assert.False(passphraseEditor.IsRevealed);
        Assert.False(FindRow(view, "Open").FindControl<Control>("PassphraseEditor")!.IsEffectivelyVisible);
        Assert.True(FindRow(view, "Open").FindControl<Button>("ConnectButton")!.IsEffectivelyVisible);

        list.SelectedItem = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Owe");
        Assert.False(FindRow(view, "Owe").FindControl<Control>("PassphraseEditor")!.IsEffectivelyVisible);
        Assert.True(FindRow(view, "Owe").FindControl<Button>("ConnectButton")!.IsEffectivelyVisible);

        list.SelectedItem = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Enterprise");
        Assert.True(FindRow(view, "Enterprise").FindControl<TextBlock>("ProvisionedProfileHint")!.IsEffectivelyVisible);
        Assert.False(FindRow(view, "Enterprise").FindControl<Button>("ConnectButton")!.IsEffectivelyVisible);

        list.SelectedItem = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Connected");
        Assert.True(FindRow(view, "Connected").FindControl<Button>("DisconnectButton")!.IsEffectivelyVisible);
        Assert.Equal(
            context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Connected").DisplayAuthentication,
            FindRow(view, "Connected").FindControl<TextBlock>("AuthenticationText")!.Text);

        Close(view, context);
    }

    [AvaloniaFact]
    public async Task Refresh_PreservesStableSelectionAndPassphraseThenClearsBothWhenNetworkDisappears()
    {
        WifiNetworkSummary[] networks = CreateNetworks();
        var service = new MainWindowViewModelTestContext.QueueNetworkStatusService(
            MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true, wifiNetworks: networks),
            MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true, wifiNetworks: networks),
            MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true, wifiNetworks: []));
        var context = new MainWindowViewModelTestContext(networkStatusService: service);
        await context.ViewModel.InitializeAsync();
        var view = Show(context);
        context.ViewModel.SelectedWifiNetwork = context.ViewModel.WifiNetworks.Single(network => network.Ssid == "Personal");
        context.ViewModel.SelectedWifiPassphrase = "secret";

        await context.ViewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Equal("Personal", context.ViewModel.SelectedWifiNetwork?.Ssid);
        Assert.Equal("secret", context.ViewModel.SelectedWifiPassphrase);

        await context.ViewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Null(context.ViewModel.SelectedWifiNetwork);
        Assert.Empty(context.ViewModel.SelectedWifiPassphrase);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<TextBlock>(view, "WifiListHeading").IsFocused);
        Close(view, context);
    }

    [AvaloniaFact]
    public async Task EmptyDiscovery_ShowsLocalizedEmptyState()
    {
        var context = CreateContext([]);
        await context.ViewModel.InitializeAsync();
        var view = Show(context);

        Assert.True(Find<TextBlock>(view, "WifiEmptyState").IsVisible);
        Assert.False(Find<ListBox>(view, "WifiNetworksList").IsVisible);

        Close(view, context);
    }

    [AvaloniaFact]
    public async Task FailedPersonalConnection_ReturnsFocusToPassphraseEditor()
    {
        var context = CreateContext([CreateNetwork("Personal", "WPA2-Personal")]);
        await context.ViewModel.InitializeAsync();
        var view = Show(context);
        Find<ListBox>(view, "WifiNetworksList").SelectedItem = context.ViewModel.WifiNetworks.Single();
        Dispatcher.UIThread.RunJobs();
        TextBox editor = Find<TextBox>(view, "PasswordEditor");

        context.ViewModel.SelectedWifiActionFeedbackText = "Connection failed";
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.IsFocused);
        Close(view, context);
    }

    private static MainWindowViewModelTestContext CreateContext(
        IReadOnlyList<WifiNetworkSummary> networks,
        string? connectedWifiSsid = null) =>
        new(networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
            MainWindowViewModelTestContext.CreateSnapshot(
                connectedWifiSsid: connectedWifiSsid,
                wifiNetworks: networks)));

    private static WifiNetworkSummary[] CreateNetworks() =>
    [
        CreateNetwork("Personal", "WPA2-Personal"),
        CreateNetwork("Open", "Open"),
        CreateNetwork("Owe", "OWE"),
        CreateNetwork("Enterprise", "WPA2-Enterprise"),
        CreateNetwork("Connected", "WPA2-Personal")
    ];

    private static WifiNetworkSummary CreateNetwork(string ssid, string authentication) => new()
    {
        Ssid = ssid,
        Authentication = authentication,
        Encryption = "AES",
        SignalStrengthPercent = 80
    };

    private static DiscoveredWifiList Show(MainWindowViewModelTestContext context)
    {
        var view = new DiscoveredWifiList { DataContext = context.ViewModel };
        var window = new Window { Content = view };
        window.Show();
        return view;
    }

    private static DiscoveredWifiRow FindRow(Control root, string ssid) =>
        root.GetVisualDescendants()
            .OfType<DiscoveredWifiRow>()
            .Single(row => row.Network?.Ssid == ssid);

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Close(Control view, MainWindowViewModelTestContext context)
    {
        (TopLevel.GetTopLevel(view) as Window)?.Close();
        context.ViewModel.Dispose();
    }
}
