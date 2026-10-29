// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Size contract of the files Foundry Connect reads as deployment-keyed <see cref="SecretEnvelope"/> JSON documents (an
/// encrypted client PFX or enterprise Wi-Fi template), shared by the generator and the Connect loader.
/// </summary>
/// <remarks>
/// The limit applies to the staged file, that is the UTF-8 envelope JSON, not to the source file: the Base64URL ciphertext
/// makes the envelope about 4/3 of the source size plus a small header. Connect treats a larger file as unavailable without
/// reading it, so the generator refuses to stage one.
/// </remarks>
public static class ConnectProtectedFileLimits
{
    /// <summary>Largest staged envelope file, in bytes (4 MiB).</summary>
    public const int MaxEnvelopeFileBytes = 4 * 1024 * 1024;
}
