// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foundry.Avalonia.Controls;
using Foundry.Avalonia.Services.Motion;

namespace Foundry.Avalonia.Tests.Visual;

public sealed class SharedControlVisualTests
{
    public static TheoryData<string, double, double, double, bool, FoundryMotionMode, FlowDirection, string>
        VisualCases => new()
        {
            { "compact-light-reduced", 960, 720, 1, false, FoundryMotionMode.Reduced, FlowDirection.LeftToRight, "compact" },
            { "standard-light-none", 1024, 768, 1, false, FoundryMotionMode.None, FlowDirection.LeftToRight, "standard" },
            { "wide-dark-full", 1366, 768, 1, true, FoundryMotionMode.Full, FlowDirection.LeftToRight, "wide" },
            { "wide-dark-rtl", 1920, 1080, 1, true, FoundryMotionMode.Reduced, FlowDirection.RightToLeft, "wide" },
            { "standard-light-200-percent", 1024, 768, 2, false, FoundryMotionMode.Reduced, FlowDirection.LeftToRight, "standard" },
        };

    [AvaloniaTheory]
    [MemberData(nameof(VisualCases))]
    public void SharedPresentationMatrixRendersReviewableFrames(
        string caseName,
        double width,
        double height,
        double renderScaling,
        bool useDarkTheme,
        FoundryMotionMode motionMode,
        FlowDirection flowDirection,
        string expectedLayoutState)
    {
        Application application = Assert.IsType<TestApp>(Application.Current);
        application.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        FoundryShell shell = CreateShell(motionMode, flowDirection);
        var window = new Window
        {
            Width = width,
            Height = height,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = shell,
        };
        window.Styles.Add(CreateAdaptiveStateStyle("compact"));
        window.Styles.Add(CreateAdaptiveStateStyle("standard"));
        window.Styles.Add(CreateAdaptiveStateStyle("wide"));
        window.SetRenderScaling(renderScaling);
        window.Show();
        try
        {
            using Bitmap frame = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
            var expectedPixelSize = new PixelSize(
                (int)Math.Round(width * renderScaling),
                (int)Math.Round(height * renderScaling));

            Assert.Equal(expectedPixelSize, frame.PixelSize);
            Assert.Equal(flowDirection, shell.FlowDirection);
            Assert.Equal(expectedLayoutState, Assert.Single(
                shell.GetVisualDescendants().OfType<AdaptiveLayoutHost>()).Tag);
            Assert.Equal(motionMode, Assert.Single(
                shell.GetVisualDescendants().OfType<FoundryProgressRing>()).MotionMode);
            Assert.All(
                shell.GetVisualDescendants().OfType<Button>(),
                button => Assert.True(button.Focusable));

            string framePath = GetFramePath(caseName);
            Directory.CreateDirectory(Path.GetDirectoryName(framePath)!);
            frame.Save(framePath, PngBitmapEncoderOptions.Default);

            Assert.True(new FileInfo(framePath).Length > 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static FoundryShell CreateShell(
        FoundryMotionMode motionMode,
        FlowDirection flowDirection)
    {
        var menu = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Button { Content = "Theme", MinHeight = 44 },
                new Button { Content = "Language", MinHeight = 44 },
                new Button { Content = "Tools", MinHeight = 44 },
                new Button { Content = "Help", MinHeight = 44 },
            },
        };
        var fields = new InformationFieldGrid
        {
            ColumnCount = 2,
            ItemsSource = new[]
            {
                "Architecture: x64",
                "Power: Connected",
                "Firmware: Detected",
                "Network: Ready",
            },
            ItemTemplate = new FuncDataTemplate<string>((value, _) =>
                new Border
                {
                    Margin = new Thickness(0, 0, 16, 12),
                    Child = new TextBlock
                    {
                        Text = value,
                        TextWrapping = TextWrapping.Wrap,
                    },
                }),
        };
        var progress = new FoundryProgressRing
        {
            Minimum = 0,
            Maximum = 100,
            Value = 64,
            AccessibleLabel = "Initialization progress",
            MotionMode = motionMode,
        };
        var status = new StatusIndicator
        {
            Kind = StatusIndicatorKind.Success,
            Title = "Network requirements are ready",
            Description = "The shared presentation primitives are rendering correctly.",
        };
        var content = new Grid
        {
            MaxWidth = 920,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RowDefinitions = new RowDefinitions("Auto,24,Auto,32,Auto,24,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = "Preparing your environment",
                    FontSize = 32,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                progress,
                status,
                fields,
            },
        };
        Grid.SetRow(progress, 2);
        Grid.SetRow(status, 4);
        Grid.SetRow(fields, 6);

        return new FoundryShell
        {
            FlowDirection = flowDirection,
            BrandContent = new TextBlock
            {
                Text = "Foundry",
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
            },
            MenuContent = menu,
            TrailingStatusContent = new TextBlock { Text = "v2.4.0" },
            Content = content,
        };
    }

    private static string GetFramePath(string caseName)
    {
        string sourceRoot = FindSourceRoot();
        string repositoryRoot = Directory.GetParent(sourceRoot)?.FullName ?? sourceRoot;
        return Path.Combine(repositoryRoot, "artifacts", "avalonia-visuals", $"{caseName}.png");
    }

    private static Style CreateAdaptiveStateStyle(string state)
    {
        var style = new Style(
            selector => selector.Is<AdaptiveLayoutHost>().Class($":{state}"));
        style.Setters.Add(new Setter(Control.TagProperty, state));
        return style;
    }

    private static string FindSourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Foundry.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Foundry source root.");
    }
}
