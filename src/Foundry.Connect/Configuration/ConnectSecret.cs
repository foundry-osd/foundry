// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Availability of one provisioned network secret.
/// </summary>
public enum ConnectSecretStatus
{
    /// <summary>The media carries no value for this secret.</summary>
    NotProvisioned,

    /// <summary>The secret was read and its value can be used.</summary>
    Available,

    /// <summary>The media carries the secret but it could not be read; only the feature that needs it is disabled.</summary>
    Unavailable,

    /// <summary>The value was used and its buffer was zeroed.</summary>
    Cleared,

    /// <summary>
    /// The media carries the secret encrypted with the deployment key; it stays unreadable until the deployment password
    /// unlocks the key.
    /// </summary>
    Locked
}

/// <summary>
/// Why a provisioned secret is <see cref="ConnectSecretStatus.Unavailable"/>.
/// </summary>
public enum ConnectSecretFailure
{
    /// <summary>The secret is not unavailable.</summary>
    None,

    /// <summary>The media secret key file is missing next to the configuration.</summary>
    MediaKeyMissing,

    /// <summary>The media secret key file exists but could not be read (I/O error or access denied).</summary>
    MediaKeyUnreadable,

    /// <summary>The media secret key file has an invalid length.</summary>
    MediaKeyInvalid,

    /// <summary>
    /// The envelope is unsupported, tampered with, not valid base64url, carries an unknown key id, or is deployment-keyed on
    /// media without a protection block; for an encrypted file, the file is also missing, too large or not an envelope.
    /// </summary>
    DecryptionFailed
}

/// <summary>
/// One provisioned network secret held in a Connect-owned character buffer that can be zeroed.
/// </summary>
/// <remarks>
/// Deliberately not a record: <see cref="ToString"/> shows only the status, so the value cannot reach logs or diagnostics
/// through string formatting. <see cref="Clear"/> zeroes the buffer; callers clear a secret as soon as its consumer no
/// longer needs it. A <see cref="ConnectSecretStatus.Locked"/> secret keeps its deployment-keyed envelope until
/// <see cref="TryUnlock"/>; <see cref="Clear"/> never drops it.
/// </remarks>
public sealed class ConnectSecret
{
    private static readonly string[] DeploymentKeyIds = [MediaSecretEnvelopeProtector.DeploymentKeyId];

    private readonly object _gate = new();
    private char[]? _value;
    private SecretEnvelope? _envelope;
    private ConnectSecretStatus _status;
    private ConnectSecretFailure _failure;

    private ConnectSecret(ConnectSecretStatus status, ConnectSecretFailure failure, char[]? value, SecretEnvelope? envelope)
    {
        _status = status;
        _failure = failure;
        _value = value;
        _envelope = envelope;
    }

    /// <summary>Gets the current availability of the secret.</summary>
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

    /// <summary>Gets why the secret is unavailable, or <see cref="ConnectSecretFailure.None"/>.</summary>
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
    /// Gets the secret characters. The span points into the buffer that <see cref="Clear"/> zeroes; do not keep it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The secret is not <see cref="ConnectSecretStatus.Available"/>.</exception>
    public ReadOnlySpan<char> Value
    {
        get
        {
            lock (_gate)
            {
                if (_status != ConnectSecretStatus.Available || _value is null)
                {
                    throw new InvalidOperationException($"The secret is not available. Status={_status}");
                }

                return _value;
            }
        }
    }

    /// <summary>Creates a secret the media does not carry.</summary>
    public static ConnectSecret NotProvisioned() => new(ConnectSecretStatus.NotProvisioned, ConnectSecretFailure.None, null, null);

    /// <summary>Creates an available secret that takes ownership of <paramref name="value"/>; the array is zeroed by <see cref="Clear"/>.</summary>
    public static ConnectSecret Available(char[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ConnectSecret(ConnectSecretStatus.Available, ConnectSecretFailure.None, value, null);
    }

    /// <summary>Creates a secret the media carries but that could not be read.</summary>
    public static ConnectSecret Unavailable(ConnectSecretFailure failure) => new(ConnectSecretStatus.Unavailable, failure, null, null);

    /// <summary>Creates a secret encrypted with the deployment key; the envelope is kept until <see cref="TryUnlock"/>.</summary>
    /// <param name="envelope">The deployment-keyed envelope.</param>
    public static ConnectSecret Locked(SecretEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new ConnectSecret(ConnectSecretStatus.Locked, ConnectSecretFailure.None, null, envelope);
    }

    /// <summary>
    /// Decrypts a <see cref="ConnectSecretStatus.Locked"/> secret with the unlocked deployment key. A secret that does not decrypt
    /// becomes <see cref="ConnectSecretStatus.Unavailable"/> with <see cref="ConnectSecretFailure.DecryptionFailed"/>; any other
    /// state is kept.
    /// </summary>
    /// <param name="deploymentKey">The deployment key; the caller keeps ownership and zeroes it.</param>
    /// <returns><see langword="true"/> when the secret became <see cref="ConnectSecretStatus.Available"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="deploymentKey"/> is not a 32-byte key; the state is not changed.</exception>
    internal bool TryUnlock(byte[] deploymentKey)
    {
        ValidateDeploymentKey(deploymentKey);
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
                _value = MediaSecretEnvelopeProtector.DecryptChars(envelope, deploymentKey, DeploymentKeyIds);
                _status = ConnectSecretStatus.Available;
                return true;
            }
            catch (CryptographicException)
            {
                // Tampered or foreign envelope. Nothing is logged here: the caller logs the unlock outcome without any value.
                _status = ConnectSecretStatus.Unavailable;
                _failure = ConnectSecretFailure.DecryptionFailed;
                return false;
            }
        }
    }

    /// <summary>
    /// Zeroes the buffer and moves an available secret to <see cref="ConnectSecretStatus.Cleared"/>. Idempotent; other states,
    /// including <see cref="ConnectSecretStatus.Locked"/> with its envelope, are kept.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_value is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_value.AsSpan()));
                _value = null;
            }

            if (_status == ConnectSecretStatus.Available)
            {
                _status = ConnectSecretStatus.Cleared;
            }
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ConnectSecret({Status})";

    /// <summary>Rejects a key that cannot be the deployment key before any state changes.</summary>
    internal static void ValidateDeploymentKey(byte[] deploymentKey)
    {
        ArgumentNullException.ThrowIfNull(deploymentKey);
        if (deploymentKey.Length != MediaSecretEnvelopeProtector.KeySizeBytes)
        {
            throw new ArgumentException($"The deployment key must be {MediaSecretEnvelopeProtector.KeySizeBytes} bytes long.", nameof(deploymentKey));
        }
    }
}
