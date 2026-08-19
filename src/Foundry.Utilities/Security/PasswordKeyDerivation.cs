// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Utilities.Security;

public static class PasswordKeyDerivation
{
    public const int DefaultIterations = 600_000;
    public const int SaltSizeBytes = 16;

    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSizeBytes);
    }

    public static byte[] DeriveKey(
        ReadOnlySpan<char> password,
        ReadOnlySpan<byte> salt,
        int iterations,
        int keySizeBytes)
    {
        if (salt.Length != SaltSizeBytes)
        {
            throw new ArgumentException($"The PBKDF2 salt must be {SaltSizeBytes} bytes.", nameof(salt));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keySizeBytes);

        byte[] key = new byte[keySizeBytes];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, key, iterations, HashAlgorithmName.SHA256);
        return key;
    }
}
