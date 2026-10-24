// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Secrets;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// Identifies what a reference publishes: the folder, the portable configuration and, when passwords are included, the passwords
/// it would carry. The host compares it with the fingerprint of the last publish, so an event that changes nothing published (a
/// per-PC value, a readiness refresh, a restart) does not create a new version.
/// </summary>
/// <remarks>The result is a SHA-256 derived from passwords: store it like a secret (in the vault) and never log it.</remarks>
public static class PublicationFingerprint
{
    /// <summary>Returns the lowercase hex SHA-256 of what a publish of <paramref name="configuration"/> would carry.</summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="includePasswords">Whether the publication includes passwords.</param>
    /// <param name="configuration">Current configuration, per-PC values included; they are stripped first.</param>
    /// <param name="vault">Vault entries; only the passwords of the configuration count, and only when included.</param>
    public static string Compute(string folder, bool includePasswords, FoundryConfigurationDocument configuration, IReadOnlyDictionary<string, string> vault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(vault);
        FoundryConfigurationDocument portable = PortableConfiguration.Strip(configuration);

        // The same passwords the engine publishes (SecretMap over the portable configuration), in a stable order.
        SortedDictionary<string, string>? secrets = includePasswords
            ? new SortedDictionary<string, string>(new Dictionary<string, string>(SecretMap.ToEntries(portable, SecretMap.FromEntries(portable, vault))),
                StringComparer.Ordinal)
            : null;
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(
            new FingerprintContent(Path.GetFullPath(folder), includePasswords, portable, secrets), ConfigurationJsonDefaults.SerializerOptions);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(content));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private sealed record FingerprintContent(string Folder, bool IncludePasswords, FoundryConfigurationDocument Configuration, SortedDictionary<string, string>? Secrets);
}
