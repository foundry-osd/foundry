// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a WinPE optional component (.cab) discovered under the ADK WinPE_OCs folder.
/// </summary>
public sealed record WinPeOptionalComponent
{
    /// <summary>
    /// Gets the component name derived from the neutral cab file name, such as <c>WinPE-WMI</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the full path to the neutral (language-independent) component cab file.
    /// </summary>
    public required string NeutralCabPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether this component is part of Foundry's recommended default selection.
    /// </summary>
    public bool IsRecommendedDefault { get; init; }

    /// <summary>
    /// Gets a short human-readable description of what the component adds, or an empty string when unknown.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
