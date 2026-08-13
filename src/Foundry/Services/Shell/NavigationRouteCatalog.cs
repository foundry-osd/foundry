// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Views;

namespace Foundry.Services.Shell;

public enum NavigationSection
{
    General,
    Expert
}

public sealed record NavigationRoute(
    string Id,
    Type PageType,
    string TitleResourceKey,
    string? DescriptionResourceKey = null,
    string? IconGlyph = null,
    NavigationSection? Section = null,
    Type? ParentPageType = null,
    bool IsAvailableWhenAdkBlocked = false);

public static class NavigationRouteCatalog
{
    public static IReadOnlyList<NavigationRoute> PrimaryRoutes { get; } =
    [
        CreatePrimary<HomeLandingPage>("Nav_HomeKey", "E80F", NavigationSection.General, true),
        CreatePrimary<AdkPage>("Nav_AdkKey", "EC7A", NavigationSection.General, true),
        CreatePrimary<GeneralConfigurationPage>("Nav_GeneralConfigurationKey", "E713", NavigationSection.General),
        CreatePrimary<StartPage>("Nav_StartKey", "E768", NavigationSection.General),
        CreatePrimary<NetworkPage>("Nav_NetworkKey", "E774", NavigationSection.Expert),
        CreatePrimary<AutopilotPage>("Nav_AutopilotKey", "E753", NavigationSection.Expert),
        CreatePrimary<CustomizationPage>("Nav_CustomizationKey", "E771", NavigationSection.Expert)
    ];

    private static IReadOnlyList<NavigationRoute> Routes { get; } =
    [
        .. PrimaryRoutes,
        CreateSettings<SettingsPage>("SettingsPage.PageTitle", null),
        CreateSettings<GeneralSettingPage>("SettingsPage_GeneralCard.Header", typeof(SettingsPage)),
        CreateSettings<ThemeSettingPage>("SettingsPage_ThemeCard.Header", typeof(SettingsPage)),
        CreateSettings<AppUpdateSettingPage>("SettingsPage_UpdateCard.Header", typeof(SettingsPage))
    ];

    public static NavigationRoute? FindById(string id) =>
        Routes.FirstOrDefault(route => string.Equals(route.Id, id, StringComparison.Ordinal));

    public static NavigationRoute? FindByPageType(Type pageType) =>
        Routes.FirstOrDefault(route => route.PageType == pageType);

    private static NavigationRoute CreatePrimary<TPage>(
        string resourcePrefix,
        string glyph,
        NavigationSection section,
        bool isAvailableWhenAdkBlocked = false) =>
        new(
            typeof(TPage).FullName!,
            typeof(TPage),
            $"{resourcePrefix}.Title",
            $"{resourcePrefix}.Description",
            glyph,
            section,
            IsAvailableWhenAdkBlocked: isAvailableWhenAdkBlocked);

    private static NavigationRoute CreateSettings<TPage>(
        string titleResourceKey,
        Type? parentPageType) =>
        new(
            typeof(TPage).FullName!,
            typeof(TPage),
            titleResourceKey,
            ParentPageType: parentPageType,
            IsAvailableWhenAdkBlocked: true);
}
