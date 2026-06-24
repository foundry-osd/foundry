// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes a generated data file staged next to pre-OOBE scripts.
/// </summary>
public sealed record PreOobeScriptDataFile
{
    /// <summary>
    /// Gets the target relative path written under the staged pre-OOBE data folder.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the UTF-8 file content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets binary file content when the data file is not text.
    /// </summary>
    public byte[]? Bytes { get; init; }

    /// <summary>
    /// Gets whether the data file contains transient sensitive material.
    /// </summary>
    public bool IsSensitive { get; init; }
}
