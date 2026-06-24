// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Localization;

namespace Foundry.Deploy.Services.Localization;

public sealed class LocalizationService : ResourceManagerLocalizationService, ILocalizationService
{
    public LocalizationService()
        : base(LocalizationText.ResourceManager, CultureInfo.CurrentUICulture, FoundrySupportedCultures.CreateCatalog())
    {
    }
}
