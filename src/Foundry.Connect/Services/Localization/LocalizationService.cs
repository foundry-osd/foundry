// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Localization;

namespace Foundry.Connect.Services.Localization;

public sealed class LocalizationService : ResourceManagerLocalizationService, ILocalizationService
{
    public LocalizationService()
        : base(
            new ResourceManager("Foundry.Connect.Strings.Resources", typeof(LocalizationService).Assembly),
            CultureInfo.CurrentUICulture,
            FoundrySupportedCultures.CreateCatalog())
    {
    }
}
