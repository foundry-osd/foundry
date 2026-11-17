// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.TestUtilities;

/// <summary>Builds and inspects <c>foundry.winpe.config.json</c> documents for media preparation tests.</summary>
internal static class WinPeConfigurationTestDocuments
{
    /// <summary>Serializes <paramref name="deploy"/> inside a document whose shared and Connect sections are not defaults.</summary>
    public static string Json(DeployRuntimeSettings deploy) => WinPeConfigurationGenerator.Serialize(new FoundryWinPeConfigurationDocument
    {
        AuthoringVersion = "26.11.1.1",
        Telemetry = new() { IsEnabled = true, InstallId = "install-id" },
        Protection = new() { IsEnabled = true, KeyDerivationAlgorithm = "pbkdf2-sha256", Iterations = 600_000, Salt = "c2FsdA" },
        Network = new() { ArtifactRootPath = @"X:\Foundry\Runtime\Roaming" },
        Connect = new()
        {
            Capabilities = new() { WifiProvisioned = true },
            Wifi = new() { IsEnabled = true, Ssid = "Corp WiFi", SecurityType = "WPA2/WPA3-Personal" }
        },
        Deploy = deploy
    });

    /// <summary>Reads the <c>deploy</c> node of a WinPE configuration.</summary>
    public static DeployRuntimeSettings Deploy(string json) =>
        JsonNode.Parse(json)!["deploy"].Deserialize<DeployRuntimeSettings>(ConfigurationJsonDefaults.SerializerOptions)!;

    /// <summary>Asserts that <paramref name="after"/> keeps every top-level section of <paramref name="before"/> except <c>deploy</c>.</summary>
    public static void AssertOnlyDeployChanged(string before, string after)
    {
        JsonObject original = JsonNode.Parse(before)!.AsObject();
        JsonObject changed = JsonNode.Parse(after)!.AsObject();
        Assert.Equal(original.Select(property => property.Key), changed.Select(property => property.Key));
        foreach ((string name, JsonNode? value) in original)
        {
            if (name != "deploy")
            {
                Assert.True(JsonNode.DeepEquals(value, changed[name]), $"The '{name}' section changed.");
            }
        }
    }
}
