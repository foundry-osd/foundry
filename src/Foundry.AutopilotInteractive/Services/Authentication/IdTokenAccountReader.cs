// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;

namespace Foundry.AutopilotInteractive.Services.Authentication;

/// <summary>
/// Reads the account name to display from an OpenID Connect id_token.
/// </summary>
/// <remarks>
/// The token comes straight from the Entra token endpoint over TLS and is used for display only, so the signature is not
/// validated. Never use this reader for an authorization decision.
/// </remarks>
public static class IdTokenAccountReader
{
    private static readonly string[] ClaimNames = ["preferred_username", "upn", "name"];

    /// <summary>
    /// Returns <c>preferred_username</c>, else <c>upn</c>, else <c>name</c>.
    /// </summary>
    /// <param name="idToken">Compact JWT, or <see langword="null"/>.</param>
    /// <returns>The account name, or <see langword="null"/> when the token is missing, malformed or has none of the claims.</returns>
    public static string? Read(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        string[] parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            if (payload.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (string name in ClaimNames)
            {
                if (payload.RootElement.TryGetProperty(name, out JsonElement claim)
                    && claim.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(claim.GetString()))
                {
                    return claim.GetString();
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
