// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Owns one copy of the 32-byte deployment key that the Foundry Connect phase hands to the Foundry Deploy phase inside the
/// Foundry.WinPE UI process, so Deploy does not ask for the deployment password again.
/// </summary>
/// <remarks>
/// The copy lives in a pinned array, so the garbage collector never leaves a moved copy behind. It can be taken once:
/// <see cref="TryTake"/> copies it out and zeroes the buffer, and <see cref="Dispose"/> zeroes an untaken key. The key is
/// never logged, persisted or put in an exception message. A taken key is not trusted: Deploy accepts it only when the
/// media's key check authenticates it. Thread-safe.
/// </remarks>
public sealed class DeploymentKeyTransfer : IDisposable
{
    /// <summary>Length of the deployment key, in bytes.</summary>
    public const int KeyLength = 32;

    private readonly Lock _gate = new();
    private readonly byte[] _key;
    private bool _available = true;

    private DeploymentKeyTransfer(byte[] key)
    {
        _key = key;
    }

    /// <summary>Gets whether the internal buffer holds only zeroes, after a take or a dispose.</summary>
    internal bool IsCleared
    {
        get
        {
            lock (_gate)
            {
                return !_key.AsSpan().ContainsAnyExcept((byte)0);
            }
        }
    }

    /// <summary>Copies <paramref name="key"/> into a new pinned buffer; the caller keeps and zeroes its own copy.</summary>
    /// <param name="key">The 32-byte deployment key.</param>
    /// <returns>A transfer that owns the copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not 32 bytes long.</exception>
    public static DeploymentKeyTransfer FromKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException("The deployment key has an unexpected length.", nameof(key));
        }

        byte[] buffer = GC.AllocateArray<byte>(KeyLength, pinned: true);
        key.CopyTo(buffer);
        return new DeploymentKeyTransfer(buffer);
    }

    /// <summary>Copies the key into <paramref name="destination"/> once and zeroes the internal buffer.</summary>
    /// <param name="destination">A 32-byte buffer the caller owns and zeroes.</param>
    /// <returns><see langword="true"/> on the first call; <see langword="false"/> once the key was taken or disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not 32 bytes long.</exception>
    public bool TryTake(Span<byte> destination)
    {
        if (destination.Length != KeyLength)
        {
            throw new ArgumentException("The destination has an unexpected length.", nameof(destination));
        }

        lock (_gate)
        {
            if (!_available)
            {
                return false;
            }

            _key.CopyTo(destination);
            Clear();
            return true;
        }
    }

    /// <summary>Zeroes an untaken key; later takes fail. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            Clear();
        }
    }

    private void Clear()
    {
        CryptographicOperations.ZeroMemory(_key);
        _available = false;
    }
}
