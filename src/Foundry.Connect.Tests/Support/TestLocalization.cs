// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Resources;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Creates the production localization service over the real Connect resources.
/// </summary>
internal static class TestLocalization
{
    /// <summary>
    /// Creates a service for the current UI culture. Tests compare with <c>GetString</c> or <c>Format</c> and never
    /// with English literals, so the culture of the build machine does not matter.
    /// </summary>
    /// <returns>A new localization service.</returns>
    public static ResourceLocalizationService Create() =>
        new(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly));
}
