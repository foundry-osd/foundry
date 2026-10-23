// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Utilities.Security;

namespace Foundry.Core.Tests.Configuration.Legacy;

/// <summary>
/// In-memory credential store. The fixture file keys each credential by its target without the path-derived scope,
/// so the copied legacy store can be read from any directory.
/// </summary>
internal sealed class FakeCredentialStore : IWindowsCredentialStore
{
    private readonly Dictionary<string, byte[]> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, byte[]> Values => values;

    /// <summary>Loads <c>local-store-credentials.json</c> (target suffix -> base64 key) for a store rooted at <paramref name="profilesRoot"/>.</summary>
    public static FakeCredentialStore LoadFixture(string profilesRoot, string credentialsPath)
    {
        var store = new FakeCredentialStore();
        string prefix = TargetPrefix(profilesRoot);
        Dictionary<string, string> entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(credentialsPath))
            ?? throw new InvalidDataException("The fixture credentials are missing.");
        foreach ((string suffix, string key) in entries)
        {
            store.values[prefix + suffix] = Convert.FromBase64String(key);
        }
        return store;
    }

    /// <summary>Legacy target prefix: the scope is the first 24 hex characters of SHA-256 over the upper-case full root path.</summary>
    public static string TargetPrefix(string profilesRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profilesRoot));
        string scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..24];
        return $"FoundryOSD/Profiles/{scope}/";
    }

    public WindowsCredential? Read(string target) => values.TryGetValue(target, out byte[]? value) ? new(value.ToArray()) : null;

    public void Write(string target, ReadOnlySpan<byte> secret, string? userName = null) => values[target] = secret.ToArray();

    public void Delete(string target) => values.Remove(target);
}
