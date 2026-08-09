// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Theme;

public interface IFoundryThemeService
{
    FoundryThemeMode CurrentTheme { get; }

    void SetTheme(FoundryThemeMode theme);
}
