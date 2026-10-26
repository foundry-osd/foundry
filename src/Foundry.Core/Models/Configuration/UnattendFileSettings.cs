// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Identifies a validated answer file whose bytes live in the authoring file library, never in the configuration.
/// </summary>
public sealed record UnattendFileSettings
{
    /// <summary>
    /// Gets the generated stable identifier used for the protected asset filename.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the operator-facing label.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 digest of the validated answer file, also published in the Deploy manifest.
    /// </summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the library copy of the answer file, whose hash equals <see cref="ContentHash"/>;
    /// <see langword="null"/> when the file could not be imported and must be imported again.
    /// </summary>
    public AuthoringFileReference? File { get; init; }
}
