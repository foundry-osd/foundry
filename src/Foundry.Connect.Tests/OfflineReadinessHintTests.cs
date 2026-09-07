// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Connect.Services.Runtime;

namespace Foundry.Connect.Tests;

public sealed class OfflineReadinessHintTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("stale", false)]
    [InlineData("architecture", false)]
    [InlineData("blocked", false)]
    [InlineData("oversized", false)]
    [InlineData("malformed", false)]
    public void OnlyCurrentBoundedReadinessEnablesOfflineHint(string scenario, bool expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), "foundry-offline-hint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Guid nonce = Guid.NewGuid();
            string path = Path.Combine(directory, "result.json");
            string json = JsonSerializer.Serialize(new
            {
                Version = 1,
                CanBrowse = scenario != "blocked",
                Nonce = scenario == "stale" ? Guid.NewGuid() : nonce,
                RuntimeIdentifier = scenario == "architecture" ? "win-x86" : "win-x64",
                Result = new
                {
                    CanContinue = false,
                    MediaId = Guid.NewGuid(),
                    ConfigurationDigest = new string('A', 64),
                    CatalogRevisions = new[] { "sha256:fixture" },
                    BlockingReasons = scenario == "blocked" ? new[] { "missing_image" } : Array.Empty<string>()
                }
            });
            File.WriteAllText(path, scenario == "oversized" ? new string(' ', 65537) : scenario == "malformed" ? "[]" : json);
            Assert.Equal(expected, OfflineReadinessHint.Read(path, nonce.ToString(), "win-x64").CanBrowse);
            Assert.Equal(20, (int)FoundryConnectExitCode.UserAborted);
            Assert.Equal(23, (int)FoundryConnectExitCode.OfflineSuccess);
        }
        finally { Directory.Delete(directory, true); }
    }
}
