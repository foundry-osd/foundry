// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Security;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Any(character => !IsBase64UrlCharacter(character)) || value.Length % 4 == 1)
        {
            throw new FormatException("The value is not valid unpadded Base64URL.");
        }

        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);

        byte[] decoded = Convert.FromBase64String(padded);
        if (!string.Equals(Encode(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException("The value is not canonical unpadded Base64URL.");
        }

        return decoded;
    }

    private static bool IsBase64UrlCharacter(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_';
    }
}
