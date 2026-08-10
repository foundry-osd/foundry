// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class EthernetOnlyViewTests
{
    [AvaloniaFact]
    public async Task NetworkReadinessView_WhenEthernetIsReady_ShowsReadinessAndTechnicalDetails()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)));
        await context.ViewModel.InitializeAsync();
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window { Content = view };

        window.Show();

        Assert.Equal(context.ViewModel.PrimaryStatusTitle, Find<TextBlock>(view, "ReadinessTitle").Text);
        Assert.Equal("Ethernet", Find<TextBlock>(view, "EthernetAdapterName").Text);
        Assert.Equal("192.0.2.10", Find<TextBlock>(view, "EthernetIpAddress").Text);
        Assert.Equal("192.0.2.1", Find<TextBlock>(view, "EthernetGateway").Text);
        Assert.True(Find<Button>(view, "ContinueButton").IsVisible);
        window.Close();
        context.ViewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task NetworkReadinessView_WhenWaiting_HidesContinueAction()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot()));
        await context.ViewModel.InitializeAsync();
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window { Content = view };

        window.Show();

        Assert.False(Find<Button>(view, "ContinueButton").IsVisible);
        window.Close();
        context.ViewModel.Dispose();
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
