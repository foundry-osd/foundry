// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Foundry.Avalonia.Services.Theme;

namespace Foundry.Avalonia.Tests.Theme;

public sealed class FoundryThemeTests
{
    private static readonly string[] BrushKeys =
    [
        "FoundryBrush.Background",
        "FoundryBrush.Surface",
        "FoundryBrush.Divider",
        "FoundryBrush.TextPrimary",
        "FoundryBrush.TextSecondary",
        "FoundryBrush.TextDisabled",
        "FoundryBrush.Accent",
        "FoundryBrush.Success",
        "FoundryBrush.Caution",
        "FoundryBrush.Critical",
        "FoundryBrush.Focus",
    ];

    private static readonly string[] MetricKeys =
    [
        "FoundrySpacing.4",
        "FoundrySpacing.8",
        "FoundrySpacing.12",
        "FoundrySpacing.16",
        "FoundrySpacing.24",
        "FoundrySpacing.32",
        "FoundrySpacing.48",
        "FoundryControlHeight.Standard",
        "FoundryControlHeight.Large",
    ];

    private static readonly string[] RadiusKeys =
    [
        "FoundryCornerRadius.Small",
        "FoundryCornerRadius.Medium",
        "FoundryCornerRadius.Large",
    ];

    private static readonly string[] TypographyKeys =
    [
        "FoundryTypography.CaptionSize",
        "FoundryTypography.BodySize",
        "FoundryTypography.SubtitleSize",
        "FoundryTypography.TitleSize",
        "FoundryTypography.DisplaySize",
    ];

    private static readonly string[] TypographyWeightKeys =
    [
        "FoundryTypography.BodyWeight",
        "FoundryTypography.SubtitleWeight",
        "FoundryTypography.TitleWeight",
        "FoundryTypography.DisplayWeight",
    ];

    [AvaloniaFact]
    public void SemanticResourcesResolveForLightAndDarkThemes()
    {
        Application application = Assert.IsType<TestApp>(Application.Current);

        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            AssertResources<IBrush>(application, variant, BrushKeys);
            AssertResources<double>(application, variant, MetricKeys);
            AssertResources<CornerRadius>(application, variant, RadiusKeys);
            AssertResources<double>(application, variant, TypographyKeys);
            AssertResources<FontWeight>(application, variant, TypographyWeightKeys);
        }
    }

    [AvaloniaFact]
    public void LightAndDarkCorePaletteRemainDistinct()
    {
        Application application = Assert.IsType<TestApp>(Application.Current);

        foreach (string key in new[]
                 {
                     "FoundryBrush.Background",
                     "FoundryBrush.Surface",
                     "FoundryBrush.TextPrimary",
                 })
        {
            SolidColorBrush light = GetBrush(application, key, ThemeVariant.Light);
            SolidColorBrush dark = GetBrush(application, key, ThemeVariant.Dark);

            Assert.NotEqual(light.Color, dark.Color);
        }
    }

    [AvaloniaTheory]
    [InlineData(FoundryThemeMode.System)]
    [InlineData(FoundryThemeMode.Light)]
    [InlineData(FoundryThemeMode.Dark)]
    public void SetThemeMapsModeWithoutClearingApplicationResources(FoundryThemeMode mode)
    {
        Application application = Assert.IsType<TestApp>(Application.Current);
        object marker = new();
        application.Resources["Test.Marker"] = marker;
        var service = new AvaloniaFoundryThemeService();

        service.SetTheme(mode);

        ThemeVariant expectedVariant = mode switch
        {
            FoundryThemeMode.Light => ThemeVariant.Light,
            FoundryThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        Assert.Equal(mode, service.CurrentTheme);
        Assert.Same(expectedVariant, application.RequestedThemeVariant);
        Assert.Same(marker, application.Resources["Test.Marker"]);
    }

    private static void AssertResources<T>(
        Application application,
        ThemeVariant variant,
        IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            Assert.True(application.TryGetResource(key, variant, out object? value), $"Missing {key} for {variant}.");
            Assert.IsAssignableFrom<T>(value);
        }
    }

    private static SolidColorBrush GetBrush(
        Application application,
        string key,
        ThemeVariant variant)
    {
        Assert.True(application.TryGetResource(key, variant, out object? value), $"Missing {key} for {variant}.");
        return Assert.IsType<SolidColorBrush>(value);
    }
}
