// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Localization;

/// <summary>
/// Describes an application UI language transition.
/// </summary>
/// <param name="oldLanguage">Previously active culture code.</param>
/// <param name="newLanguage">Newly active culture code.</param>
public sealed class ApplicationLanguageChangedEventArgs(string oldLanguage, string newLanguage) : EventArgs
{
    /// <summary>
    /// Gets the previously active culture code.
    /// </summary>
    public string OldLanguage { get; } = oldLanguage;

    /// <summary>
    /// Gets the newly active culture code.
    /// </summary>
    public string NewLanguage { get; } = newLanguage;
}
