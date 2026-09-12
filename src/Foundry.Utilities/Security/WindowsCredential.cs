// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Utilities.Security;

/// <summary>
/// Owns credential bytes returned by a store and erases them when disposed.
/// </summary>
/// <param name="secret">Buffer whose ownership is transferred to this credential without copying.</param>
/// <param name="userName">Optional account metadata associated with the secret.</param>
public sealed class WindowsCredential(byte[] secret, string? userName = null) : IDisposable
{
    /// <summary>
    /// Gets the owned secret buffer. Do not retain or use it after disposing this credential.
    /// </summary>
    public byte[] Secret { get; } = secret ?? throw new ArgumentNullException(nameof(secret));

    public string? UserName { get; } = userName;

    /// <summary>
    /// Erases the owned bytes; repeated disposal is harmless.
    /// </summary>
    public void Dispose() => CryptographicOperations.ZeroMemory(Secret);
}
