// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ConnectAccessibilityTests
{
    [AvaloniaFact]
    public async Task ArabicCulture_MirrorsTheRootAndNetworkRowsExposeSemanticNames()
    {
        WifiNetworkSummary[] networks =
        [
            new()
            {
                Ssid = "Foundry",
                Authentication = "WPA2-Personal",
                Encryption = "AES",
                SignalStrengthPercent = 80
            }
        ];
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(wifiNetworks: networks)));
        context.LocalizationService.SetCulture(CultureInfo.GetCultureInfo("ar-SA"));
        await context.ViewModel.InitializeAsync();
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window { Content = view, FlowDirection = context.ViewModel.UiFlowDirection };
        window.Show();

        DiscoveredWifiRow row = view.GetVisualDescendants().OfType<DiscoveredWifiRow>().Single();
        Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
        Assert.Contains("Foundry", AutomationProperties.GetName(row));
        Assert.Contains("80", AutomationProperties.GetName(row));

        window.Close();
        context.ViewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task StatusAndFailureText_UseLiveRegionSemantics()
    {
        var context = new MainWindowViewModelTestContext();
        await context.ViewModel.InitializeAsync();
        context.ViewModel.ProvisionedWifiActionFeedbackText = "Failed";
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window { Content = view };
        window.Show();

        Assert.Contains(
            view.GetVisualDescendants(),
            control => AutomationProperties.GetLiveSetting(control) == AutomationLiveSetting.Polite);
        Assert.Contains(
            view.GetVisualDescendants(),
            control => AutomationProperties.GetLiveSetting(control) == AutomationLiveSetting.Assertive);

        window.Close();
        context.ViewModel.Dispose();
    }
}
