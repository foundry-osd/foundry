// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml;

namespace Foundry.Services.Appearance;

public interface IAppThemeService
{
    ElementTheme ElementTheme { get; }

    AppBackdropKind Backdrop { get; }

    void Initialize(Window window, FrameworkElement rootElement);

    void SetElementTheme(ElementTheme theme);

    void SetBackdrop(AppBackdropKind backdrop);
}
