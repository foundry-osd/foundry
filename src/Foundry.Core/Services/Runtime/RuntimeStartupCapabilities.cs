// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Services.Runtime;

/// <summary>Negotiates advertised protocol versions without guessing support from product versions.</summary>
public static class RuntimeStartupCapabilities
{
    /// <summary>Reads the manifest adjacent to the executable; inaccessible or malformed manifests are invalid.</summary>
    public static StartupCapabilityResult Negotiate(string executablePath)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executablePath))!, StartupProtocol.ManifestFileName);
            byte[] content;
            try { content = RuntimeStartupFile.Read(path); }
            catch (FileNotFoundException) { return new(StartupCapabilityMode.Legacy); }
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (!RuntimeStartupFile.HasUniqueProperties(root) ||
                !root.TryGetProperty("protocolVersions", out JsonElement versions) ||
                versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() is < 1 or > 16)
                return new(StartupCapabilityMode.Invalid);

            var supported = new HashSet<int>();
            foreach (JsonElement version in versions.EnumerateArray())
            {
                if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number <= 0 || !supported.Add(number))
                    return new(StartupCapabilityMode.Invalid);
            }
            return supported.Contains(StartupProtocol.Version)
                ? new(StartupCapabilityMode.Supported, StartupProtocol.Version)
                : new(StartupCapabilityMode.Incompatible);
        }
        catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception))
        {
            return new(StartupCapabilityMode.Invalid);
        }
    }
}
