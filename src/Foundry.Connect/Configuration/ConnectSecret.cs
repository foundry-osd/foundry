// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    /// <summary>The media carries the secret but it could not be read; only the feature that needs it is disabled (D9).</summary>
    Unavailable,

    /// <summary>The value was used and its buffer was zeroed.</summary>
    Cleared
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

    /// <summary>The envelope is unsupported, tampered with, or not valid base64url.</summary>
    DecryptionFailed
}

/// <summary>
/// One provisioned network secret held in a Connect-owned character buffer that can be zeroed (T15).
/// </summary>
/// <remarks>
/// Deliberately not a record: <see cref="ToString"/> shows only the status, so the value cannot reach logs or diagnostics
/// through string formatting. <see cref="Clear"/> zeroes the buffer; callers clear a secret as soon as its consumer no
/// longer needs it (L2-6).
/// </remarks>
public sealed class ConnectSecret
{
    private readonly object _gate = new();
    private char[]? _value;
    private ConnectSecretStatus _status;

    private ConnectSecret(ConnectSecretStatus status, ConnectSecretFailure failure, char[]? value)
    {
        _status = status;
        Failure = failure;
        _value = value;
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
    public ConnectSecretFailure Failure { get; }

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
    public static ConnectSecret NotProvisioned() => new(ConnectSecretStatus.NotProvisioned, ConnectSecretFailure.None, null);

    /// <summary>Creates an available secret that takes ownership of <paramref name="value"/>; the array is zeroed by <see cref="Clear"/>.</summary>
    public static ConnectSecret Available(char[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ConnectSecret(ConnectSecretStatus.Available, ConnectSecretFailure.None, value);
    }

    /// <summary>Creates a secret the media carries but that could not be read.</summary>
    public static ConnectSecret Unavailable(ConnectSecretFailure failure) => new(ConnectSecretStatus.Unavailable, failure, null);

    /// <summary>
    /// Zeroes the buffer and moves an available secret to <see cref="ConnectSecretStatus.Cleared"/>. Idempotent; other states are kept.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_value is not null)
            {
                Array.Clear(_value);
                _value = null;
            }

            if (_status == ConnectSecretStatus.Available)
            {
                _status = ConnectSecretStatus.Cleared;
            }
        }
    }

    /// <summary>
    /// Copies the value into a managed string for the legacy bootstrap path, which cannot be zeroed.
    /// </summary>
    /// <remarks>Temporary for <c>NetworkBootstrapService</c>; deleted with it.</remarks>
    internal string? ToLegacyString()
    {
        lock (_gate)
        {
            return _status == ConnectSecretStatus.Available && _value is not null ? new string(_value) : null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ConnectSecret({Status})";
}
