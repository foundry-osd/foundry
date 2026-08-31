// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Application;

/// <summary>
/// Provides the application dialog style that includes the standard WinUI presentation transitions.
/// </summary>
internal static class ContentDialogStyleProvider
{
    /// <summary>
    /// Gets the default style for application-owned content dialogs.
    /// </summary>
    public static Style DefaultStyle =>
        (Style)Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"];
}
