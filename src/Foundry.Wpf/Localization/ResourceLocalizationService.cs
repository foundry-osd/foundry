// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Localization;

namespace Foundry.Wpf.Localization;

/// <summary>
/// Resolves localized strings for a WPF runtime application from the resource manager it owns,
/// starting from the current UI culture and the shared supported culture catalog.
/// </summary>
public sealed class ResourceLocalizationService : ResourceManagerLocalizationService, ILocalizationService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceLocalizationService" /> class.
    /// </summary>
    /// <param name="resourceManager">Resource manager that owns the application strings.</param>
    public ResourceLocalizationService(ResourceManager resourceManager)
        : base(resourceManager, CultureInfo.CurrentUICulture, FoundrySupportedCultures.CreateCatalog())
    {
    }
}
