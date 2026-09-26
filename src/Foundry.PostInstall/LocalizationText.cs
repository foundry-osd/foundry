// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Localization;

namespace Foundry.PostInstall;

public static class LocalizationText
{
    public static ResourceManager ResourceManager { get; } = new("Foundry.PostInstall.Strings.Resources", typeof(LocalizationText).Assembly);

    public static ResourceManagerLocalizationService Create(string? culture = null)
    {
        var catalog = FoundrySupportedCultures.CreateCatalog();
        string code = catalog.MatchPreferredCulture([culture ?? CultureInfo.CurrentUICulture.Name]);
        return new(ResourceManager, CultureInfo.GetCultureInfo(code), catalog);
    }
}
