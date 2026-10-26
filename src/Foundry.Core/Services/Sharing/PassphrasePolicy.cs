// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.Sharing;

/// <summary>Outcome of checking a sharing passphrase against <see cref="PassphrasePolicy"/>.</summary>
public enum PassphraseValidation
{
    /// <summary>The passphrase can protect an export or a publication.</summary>
    Valid,

    /// <summary>The passphrase has fewer than <see cref="PassphrasePolicy.MinimumLength"/> characters.</summary>
    TooShort,

    /// <summary>The passphrase repeats one single character.</summary>
    Repetitive
}

/// <summary>
/// Passphrase rules shared by configuration exports and publications: at least <see cref="MinimumLength"/> characters
/// and not one repeated character. Strength beyond that is left to the user; no word list is involved.
/// </summary>
public static class PassphrasePolicy
{
    /// <summary>Minimum number of characters of an accepted passphrase.</summary>
    public const int MinimumLength = 12;

    // Unambiguous characters only: no 0, O, 1, l or I (nor i, L, o) so a generated passphrase can be read aloud or copied by hand.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
    private const int GeneratedLength = 20;
    private const int GroupLength = 4;

    /// <summary>Checks a passphrase against the sharing policy.</summary>
    public static PassphraseValidation Validate(ReadOnlySpan<char> passphrase)
    {
        if (passphrase.Length < MinimumLength)
        {
            return PassphraseValidation.TooShort;
        }

        return passphrase.ContainsAnyExcept(passphrase[0]) ? PassphraseValidation.Valid : PassphraseValidation.Repetitive;
    }

    /// <summary>
    /// Generates a random passphrase of 20 characters from an unambiguous alphabet, grouped by 4 with dashes
    /// (for example <c>xK7m-Qp2v-...</c>). The result always satisfies <see cref="Validate"/>.
    /// </summary>
    public static string Generate()
    {
        string characters;
        do
        {
            characters = RandomNumberGenerator.GetString(Alphabet, GeneratedLength);
        }
        while (Validate(characters) != PassphraseValidation.Valid);

        return string.Join('-', characters.Chunk(GroupLength).Select(group => new string(group)));
    }
}
