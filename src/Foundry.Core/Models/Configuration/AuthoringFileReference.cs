// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Identifies a small authoring file held in the local authoring file library instead of a local path.</summary>
public sealed record AuthoringFileReference
{
    /// <summary>Gets the lowercase hexadecimal SHA-256 of the file content; the only identity of the file.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Gets the sanitized file name shown to the user and used as the stored name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets the file length in bytes.</summary>
    public long Length { get; init; }
}
