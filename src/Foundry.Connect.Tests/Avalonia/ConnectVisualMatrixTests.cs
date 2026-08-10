// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foundry.Avalonia.Services.Motion;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ConnectVisualMatrixTests
{
    public static TheoryData<string, double, double, double, bool, string> VisualCases => new()
    {
        { "compact-light-fr", 960, 720, 1, false, "fr-FR" },
        { "standard-light-de", 1024, 768, 1, false, "de-DE" },
        { "wide-dark-ja", 1366, 768, 1, true, "ja-JP" },
        { "wide-dark-ar", 1366, 768, 1, true, "ar-SA" },
        { "standard-light-he-200-percent", 1024, 768, 2, false, "he-IL" }
    };

    [AvaloniaTheory]
    [InlineData(900, "CompactLayout")]
    [InlineData(1100, "StandardLayout")]
    [InlineData(1400, "WideLayout")]
    public void NetworkReadinessView_SelectsTheApprovedComposition(double width, string visibleLayout)
    {
        var context = new MainWindowViewModelTestContext();
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window { Width = width, Height = 768, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Control expected = view.GetVisualDescendants().OfType<Control>().Single(control => control.Name == visibleLayout);
        Assert.True(expected.IsEffectivelyVisible);
        Assert.Single(
            view.GetVisualDescendants().OfType<Control>(),
            control => (control.Name is "CompactLayout" or "StandardLayout" or "WideLayout") && control.IsEffectivelyVisible);

        window.Close();
        context.ViewModel.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(FoundryMotionMode.Full, true)]
    [InlineData(FoundryMotionMode.Reduced, false)]
    [InlineData(FoundryMotionMode.None, false)]
    public void DiscoveredRow_ExpandsOnlyWithFullMotion(FoundryMotionMode mode, bool hasTransitions)
    {
        var row = new DiscoveredWifiRow { MotionMode = mode };
        var window = new Window { Content = row };
        window.Show();
        Grid actions = row.FindControl<Grid>("ConnectActions")!;

        Assert.Equal(hasTransitions, actions.Transitions?.Count > 0);

        window.Close();
    }

    [AvaloniaTheory]
    [MemberData(nameof(VisualCases))]
    public async Task PresentationMatrixRendersReviewableFrames(
        string caseName,
        double width,
        double height,
        double renderScaling,
        bool useDarkTheme,
        string cultureName)
    {
        Application.Current!.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        WifiNetworkSummary[] networks =
        [
            new()
            {
                Ssid = "Foundry-Lab",
                Authentication = "WPA2-Personal",
                Encryption = "AES",
                SignalStrengthPercent = 84
            },
            new()
            {
                Ssid = "Guest",
                Authentication = "Open",
                SignalStrengthPercent = 58
            }
        ];
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(
                    hasInternetAccess: true,
                    wifiNetworks: networks)));
        context.LocalizationService.SetCulture(CultureInfo.GetCultureInfo(cultureName));
        await context.ViewModel.InitializeAsync();
        var view = new NetworkReadinessView { DataContext = context.ViewModel };
        var window = new Window
        {
            Width = width,
            Height = height,
            FlowDirection = context.ViewModel.UiFlowDirection,
            Content = view
        };
        window.SetRenderScaling(renderScaling);
        window.Show();

        using Bitmap frame = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
        Assert.Equal(
            new PixelSize((int)Math.Round(width * renderScaling), (int)Math.Round(height * renderScaling)),
            frame.PixelSize);
        string framePath = Path.Combine(FindRepositoryRoot(), "artifacts", "avalonia-visuals", $"connect-{caseName}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(framePath)!);
        frame.Save(framePath, PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(framePath).Length > 0);

        window.Close();
        context.ViewModel.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
