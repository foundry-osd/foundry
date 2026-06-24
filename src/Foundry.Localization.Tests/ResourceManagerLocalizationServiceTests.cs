// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Foundry.Localization;

namespace Foundry.Localization.Tests;

public sealed class ResourceManagerLocalizationServiceTests
{
    [Fact]
    public void SetCulture_UpdatesCurrentCulturesAndRaisesSingleLanguageChangedEvent()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            ResourceManagerLocalizationService service = CreateService("en-US");
            ApplicationLanguageChangedEventArgs? eventArgs = null;

            service.LanguageChanged += (_, args) => eventArgs = args;

            service.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));

            Assert.Equal("fr-FR", service.CurrentCulture.Name);
            Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
            Assert.Equal("fr-FR", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("fr-FR", CultureInfo.DefaultThreadCurrentCulture?.Name);
            Assert.Equal("fr-FR", CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.NotNull(eventArgs);
            Assert.Equal("en-US", eventArgs.OldLanguage);
            Assert.Equal("fr-FR", eventArgs.NewLanguage);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }

    [Fact]
    public void Constructor_WhenInitialCultureMatchesSupportedLanguageFamily_UsesConfiguredCulture()
    {
        SupportedCultureCatalog catalog = new(
            "en-US",
            [
                new SupportedCultureDefinition("en-US", "Language.English", 10),
                new SupportedCultureDefinition("fr-FR", "Language.French", 20)
            ]);

        ResourceManagerLocalizationService service = CreateService("fr-CA", catalog);

        Assert.Equal("fr-FR", service.CurrentCulture.Name);
    }

    [Fact]
    public void SetCulture_WhenCultureMatchesSupportedLanguageFamily_AppliesConfiguredCulture()
    {
        ResourceManagerLocalizationService service = CreateService("en-US");
        ApplicationLanguageChangedEventArgs? eventArgs = null;

        service.LanguageChanged += (_, args) => eventArgs = args;

        service.SetCulture(CultureInfo.GetCultureInfo("fr-CA"));

        Assert.Equal("fr-FR", service.CurrentCulture.Name);
        Assert.NotNull(eventArgs);
        Assert.Equal("en-US", eventArgs.OldLanguage);
        Assert.Equal("fr-FR", eventArgs.NewLanguage);
    }

    [Fact]
    public void SetCulture_WhenCultureDoesNotChange_DoesNotRaiseLanguageChanged()
    {
        ResourceManagerLocalizationService service = CreateService("en-US");
        int changeCount = 0;

        service.LanguageChanged += (_, _) => changeCount++;

        service.SetCulture(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void Strings_ReturnsLocalizedValueAndNotifiesIndexerChange()
    {
        ResourceManagerLocalizationService service = CreateService("en-US");
        List<string?> changedProperties = [];

        service.Strings.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.Equal("Hello", service.Strings["Greeting"]);

        service.SetCulture(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("Bonjour", service.Strings["Greeting"]);
        Assert.Contains("Item[]", changedProperties);
    }

    [Fact]
    public void GetString_WhenKeyIsMissing_ReturnsKey()
    {
        ResourceManagerLocalizationService service = CreateService("en-US");

        string result = service.GetString("Missing.Key");

        Assert.Equal("Missing.Key", result);
    }

    [Fact]
    public void CreateSupportedCultureOptions_UsesCurrentCultureAndLocalizedDisplayNames()
    {
        SupportedCultureCatalog catalog = new(
            "de-DE",
            [
                new SupportedCultureDefinition("de-DE", "Language.German", 10),
                new SupportedCultureDefinition("es-ES", "Language.Spanish", 20),
                new SupportedCultureDefinition("it-IT", "Language.Italian", 30)
            ]);
        ResourceManagerLocalizationService service = CreateService("it-IT", catalog);

        IReadOnlyList<SupportedCultureOption> options = service.CreateSupportedCultureOptions();

        Assert.Equal(["de-DE", "es-ES", "it-IT"], options.Select(option => option.Code));
        Assert.Equal("Language.Italian", options.Single(option => option.Code == "it-IT").DisplayName);
        Assert.True(options.Single(option => option.Code == "it-IT").IsSelected);
    }

    private static ResourceManagerLocalizationService CreateService(string cultureName)
    {
        return CreateService(cultureName, CreateTestCatalog());
    }

    private static ResourceManagerLocalizationService CreateService(string cultureName, SupportedCultureCatalog catalog)
    {
        ResourceManager resourceManager = new(
            "Foundry.Localization.Tests.Strings.Resources",
            typeof(ResourceManagerLocalizationServiceTests).Assembly);

        return new ResourceManagerLocalizationService(
            resourceManager,
            CultureInfo.GetCultureInfo(cultureName),
            catalog);
    }

    private static SupportedCultureCatalog CreateTestCatalog()
    {
        return new SupportedCultureCatalog(
            "en-US",
            [
                new SupportedCultureDefinition("en-US", "Language.English", 10),
                new SupportedCultureDefinition("fr-FR", "Language.French", 20)
            ]);
    }
}
