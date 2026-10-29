// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Encrypted file content read from the media (a client PFX, an enterprise Wi-Fi template with key material), held in a
/// Connect-owned byte buffer that can be zeroed.
/// </summary>
/// <remarks>
/// Schema 6 stages these files as deployment-keyed <see cref="SecretEnvelope"/> JSON documents (L4-5, L4-6). The loader keeps
/// the envelope while the content is <see cref="ConnectSecretStatus.Locked"/>; <see cref="TryUnlock"/> decrypts it with the
/// unlocked deployment key. Deliberately not a record: <see cref="ToString"/> shows only the status, never bytes.
/// </remarks>
public sealed class ConnectProtectedContent
{
    private readonly object _gate = new();
    private byte[]? _content;
    private SecretEnvelope? _envelope;
    private ConnectSecretStatus _status;
    private ConnectSecretFailure _failure;

    private ConnectProtectedContent(ConnectSecretStatus status, ConnectSecretFailure failure, SecretEnvelope? envelope)
    {
        _status = status;
        _failure = failure;
        _envelope = envelope;
    }

    /// <summary>Gets the current availability of the content.</summary>
    public ConnectSecretStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Gets why the content is unavailable, or <see cref="ConnectSecretFailure.None"/>.</summary>
    public ConnectSecretFailure Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    /// <summary>
    /// Gets the decrypted bytes. The span points into the buffer that <see cref="Clear"/> zeroes; do not keep it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The content is not <see cref="ConnectSecretStatus.Available"/>.</exception>
    public ReadOnlySpan<byte> Content
    {
        get
        {
            lock (_gate)
            {
                if (_status != ConnectSecretStatus.Available || _content is null)
                {
                    throw new InvalidOperationException($"The protected content is not available. Status={_status}");
                }

                return _content;
            }
        }
    }

    /// <summary>Creates content the media does not carry encrypted (no file, or a plain file read through its path).</summary>
    public static ConnectProtectedContent NotProvisioned() => new(ConnectSecretStatus.NotProvisioned, ConnectSecretFailure.None, null);

    /// <summary>Creates content encrypted with the deployment key; the envelope is kept until <see cref="TryUnlock"/>.</summary>
    /// <param name="envelope">The deployment-keyed envelope read from the encrypted file.</param>
    public static ConnectProtectedContent Locked(SecretEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new ConnectProtectedContent(ConnectSecretStatus.Locked, ConnectSecretFailure.None, envelope);
    }

    /// <summary>Creates content the media carries but that cannot be used.</summary>
    public static ConnectProtectedContent Unavailable(ConnectSecretFailure failure) => new(ConnectSecretStatus.Unavailable, failure, null);

    /// <summary>
    /// Decrypts <see cref="ConnectSecretStatus.Locked"/> content with the unlocked deployment key. Content that does not decrypt
    /// becomes <see cref="ConnectSecretStatus.Unavailable"/> with <see cref="ConnectSecretFailure.DecryptionFailed"/>; any other
    /// state is kept.
    /// </summary>
    /// <param name="deploymentKey">The deployment key; the caller keeps ownership and zeroes it.</param>
    /// <returns><see langword="true"/> when the content became <see cref="ConnectSecretStatus.Available"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="deploymentKey"/> is not a 32-byte key; the state is not changed.</exception>
    internal bool TryUnlock(byte[] deploymentKey)
    {
        ConnectSecret.ValidateDeploymentKey(deploymentKey);
        lock (_gate)
        {
            if (_status != ConnectSecretStatus.Locked || _envelope is null)
            {
                return false;
            }

            SecretEnvelope envelope = _envelope;
            _envelope = null;
            try
            {
                _content = MediaSecretEnvelopeProtector.DecryptBytes(envelope, deploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
                _status = ConnectSecretStatus.Available;
                return true;
            }
            catch (CryptographicException)
            {
                // Tampered or foreign envelope; no file name or byte is logged.
                _status = ConnectSecretStatus.Unavailable;
                _failure = ConnectSecretFailure.DecryptionFailed;
                return false;
            }
        }
    }

    /// <summary>
    /// Zeroes the buffer and moves available content to <see cref="ConnectSecretStatus.Cleared"/>. Idempotent; other states,
    /// including <see cref="ConnectSecretStatus.Locked"/> with its envelope, are kept.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_content is not null)
            {
                CryptographicOperations.ZeroMemory(_content);
                _content = null;
            }

            if (_status == ConnectSecretStatus.Available)
            {
                _status = ConnectSecretStatus.Cleared;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ConnectProtectedContent({Status})";
}
