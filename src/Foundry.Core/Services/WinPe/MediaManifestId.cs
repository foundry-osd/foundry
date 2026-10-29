// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Derives media manifest identifiers from manifest content, so the same configuration always produces the same manifest bytes and id.
/// </summary>
internal static class MediaManifestId
{
    private const int HexLength = 32;

    /// <summary>
    /// Returns the first 32 lowercase hexadecimal characters of the SHA-256 digest of the canonical manifest content.
    /// </summary>
    /// <param name="canonicalContent">
    /// The manifest serialized with its own identifier left empty, using the serializer options of the final manifest.
    /// </param>
    /// <returns>An identifier that is also a valid manifest file name stem.</returns>
    public static string From(ReadOnlySpan<byte> canonicalContent) =>
        Convert.ToHexStringLower(SHA256.HashData(canonicalContent))[..HexLength];
}
