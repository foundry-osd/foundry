// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Deploy.Services.System;

public static class ComputerNameRandomValueGenerator
{
    private const string AllowedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        Span<char> value = length <= 64 ? stackalloc char[length] : new char[length];
        for (int index = 0; index < value.Length; index++)
        {
            value[index] = AllowedCharacters[RandomNumberGenerator.GetInt32(AllowedCharacters.Length)];
        }

        return new string(value);
    }
}
