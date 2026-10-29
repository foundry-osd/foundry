// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Foundry.Connect.Network;

/// <summary>
/// Identifies a Wi-Fi network by the raw SSID bytes broadcast by the access point.
/// </summary>
/// <remarks>
/// An SSID is a sequence of 1 to 32 bytes, not text: equality and hashing compare bytes, so two networks that
/// differ only by case or by invalid UTF-8 sequences stay distinct. The text form is for display only and is never
/// trimmed. The SSID is a sensitive name: <see cref="ToString"/> never returns it, and it must never be logged, neither in
/// message text nor as a structured property, because the remote Logs stream mirrors every log property.
/// </remarks>
public sealed class WifiSsid : IEquatable<WifiSsid>
{
    /// <summary>
    /// Maximum SSID length in bytes (IEEE 802.11, <c>DOT11_SSID_MAX_LENGTH</c>).
    /// </summary>
    public const int MaximumLength = 32;

    private readonly byte[] _bytes;

    /// <summary>
    /// Creates an SSID from raw bytes.
    /// </summary>
    /// <param name="bytes">SSID bytes, 1 to <see cref="MaximumLength"/> bytes. The bytes are copied.</param>
    /// <exception cref="ArgumentOutOfRangeException">The span is empty or longer than <see cref="MaximumLength"/>.</exception>
    public WifiSsid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                bytes.Length,
                $"An SSID must contain between 1 and {MaximumLength} bytes.");
        }

        _bytes = bytes.ToArray();
        Hex = Convert.ToHexString(_bytes);
        DisplayName = Encoding.UTF8.GetString(_bytes);
    }

    /// <summary>
    /// Gets the raw SSID bytes.
    /// </summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>
    /// Gets the upper-case hexadecimal representation of the SSID bytes.
    /// </summary>
    public string Hex { get; }

    /// <summary>
    /// Gets the UTF-8 decoding of the SSID for display. Invalid sequences become replacement characters and
    /// surrounding spaces are kept.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Creates an SSID from text using its UTF-8 bytes, without trimming.
    /// </summary>
    /// <param name="text">SSID text.</param>
    /// <returns>The SSID.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The UTF-8 form is empty or longer than <see cref="MaximumLength"/> bytes.</exception>
    public static WifiSsid FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new WifiSsid(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Creates an SSID from its hexadecimal representation.
    /// </summary>
    /// <param name="hex">Hexadecimal digits, upper or lower case.</param>
    /// <returns>The SSID.</returns>
    /// <exception cref="FormatException">The value is not valid hexadecimal.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The decoded length is not between 1 and <see cref="MaximumLength"/> bytes.</exception>
    public static WifiSsid FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        return new WifiSsid(Convert.FromHexString(hex));
    }

    /// <summary>
    /// Creates an SSID when the length is valid.
    /// </summary>
    /// <param name="bytes">Candidate SSID bytes.</param>
    /// <param name="ssid">The SSID, or <see langword="null"/> when the length is 0 or above <see cref="MaximumLength"/>.</param>
    /// <returns><see langword="true"/> when an SSID was created.</returns>
    public static bool TryFromBytes(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out WifiSsid? ssid)
    {
        if (bytes.Length is < 1 or > MaximumLength)
        {
            ssid = null;
            return false;
        }

        ssid = new WifiSsid(bytes);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(WifiSsid? other)
    {
        return other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as WifiSsid);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns a description that never contains the network name.
    /// </summary>
    /// <returns>The byte length only.</returns>
    public override string ToString()
    {
        return $"WifiSsid({_bytes.Length} bytes)";
    }

    /// <summary>
    /// Compares two SSIDs by bytes.
    /// </summary>
    public static bool operator ==(WifiSsid? left, WifiSsid? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    /// Compares two SSIDs by bytes.
    /// </summary>
    public static bool operator !=(WifiSsid? left, WifiSsid? right)
    {
        return !(left == right);
    }
}
