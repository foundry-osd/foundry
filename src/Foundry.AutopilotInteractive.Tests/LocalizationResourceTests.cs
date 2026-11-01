// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;

namespace Foundry.AutopilotInteractive.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] CultureNames =
    [
        "ar-SA",
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "en-US",
        "es-ES",
        "es-MX",
        "et-EE",
        "fi-FI",
        "fr-CA",
        "fr-FR",
        "he-IL",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "ja-JP",
        "ko-KR",
        "lt-LT",
        "lv-LV",
        "nb-NO",
        "nl-NL",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "ro-RO",
        "ru-RU",
        "sk-SK",
        "sl-SI",
        "sr-Latn-RS",
        "sv-SE",
        "th-TH",
        "tr-TR",
        "uk-UA",
        "zh-CN",
        "zh-TW"
    ];

    // Texts of the shell, the About dialog and the configuration error: the parity tests keep the key sets equal, this keeps them filled.
    private static readonly string[] ShellKeys =
    [
        "About.DescriptionLine1", "About.DescriptionLine2", "About.Footer", "About.Title", "App.Name", "Common.Close",
        "Common.VersionFormat", "Config.LoadFailed", "Footer.Unknown", "Menu.About", "Menu.Debug", "Menu.Language", "Menu.Theme",
        "Theme.Dark", "Theme.Light", "Theme.System"
    ];

    public static TheoryData<string> SupportedCultures => new(CultureNames);

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void ResourceSet_ContainsShellTextsForEveryCulture(string cultureName)
    {
        ResourceSet resourceSet = GetResourceSet(cultureName);

        Assert.Equal("Foundry Autopilot Interactive", resourceSet.GetString("App.Name"));
        foreach (string key in ShellKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"{cultureName} {key}");
        }
    }

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void MenuHeaders_HaveAccessKeys(string cultureName)
    {
        ResourceSet resourceSet = GetResourceSet(cultureName);

        foreach (string key in new[] { "Menu.About", "Menu.Debug", "Menu.Language", "Menu.Theme" })
        {
            Assert.Contains("_", resourceSet.GetString(key), StringComparison.Ordinal);
        }
    }

    private static ResourceSet GetResourceSet(string cultureName)
    {
        ResourceManager resourceManager = new("Foundry.AutopilotInteractive.Strings.Resources", typeof(App).Assembly);
        return resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
    }
}
