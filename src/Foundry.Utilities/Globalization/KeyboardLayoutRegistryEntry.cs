// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Raw keyboard layout values read from the Windows <c>Keyboard Layouts</c> registry key.
/// </summary>
/// <param name="Id">Registry subkey name, expected to be a KLID.</param>
/// <param name="LayoutFile">The <c>Layout File</c> value.</param>
/// <param name="LayoutText">The <c>Layout Text</c> value, an English fallback name.</param>
/// <param name="DisplayName">The resolved <c>Layout Display Name</c> value, or <see langword="null"/> when it could not be resolved.</param>
public sealed record KeyboardLayoutRegistryEntry(string Id, string? LayoutFile, string? LayoutText, string? DisplayName);
