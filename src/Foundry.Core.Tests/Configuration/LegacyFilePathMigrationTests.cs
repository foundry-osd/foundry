// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class LegacyFilePathMigrationTests : IDisposable
{
    private const string UnattendId = "0123456789abcdef0123456789abcdef";
    private readonly TemporaryDirectory temp = new();

    [Fact]
    public void Extract_Schema17DocumentWithPaths_ReturnsPathsAndRemovesMembers()
    {
        JsonNode root = JsonNode.Parse(LegacyDocument(17))!;

        LegacyFilePaths paths = FoundryConfigurationMigration.ExtractLegacyFilePaths(root);

        Assert.Equal(@"C:\Sources\wired.xml", paths.Dot1xProfileTemplate);
        Assert.Equal(@"C:\Sources\wired.pfx", paths.Dot1xCertificate);
        Assert.Equal(@"C:\Sources\wifi.xml", paths.WifiEnterpriseProfileTemplate);
        Assert.Equal(@"C:\Sources\wifi.cer", paths.WifiCertificate);
        Assert.Equal(@"C:\Sources\autopilot.pfx", paths.AutopilotPfx);
        Assert.Equal(@"C:\Sources\answer.xml", paths.UnattendSourcePaths[UnattendId]);
        string json = root.ToJsonString();
        Assert.DoesNotContain("profileTemplatePath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("certificatePath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pfxPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sourcePath", json, StringComparison.Ordinal);
        Assert.Equal(UnattendId, root["unattend"]!["files"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Extract_Schema18DocumentWithPaths_ReturnsPaths()
    {
        // Released v26.10.18.1 shape: the boot media certificate was session-only, so only network and answer-file paths exist.
        JsonNode root = JsonNode.Parse("""
            {
              "schemaVersion": 18,
              "network": {
                "dot1x": { "isEnabled": true, "profileTemplatePath": "C:\\Sources\\wired.xml", "requiresCertificate": false },
                "wifiProvisioned": true,
                "wifi": { "isEnabled": true, "ssid": "Corp", "hasEnterpriseProfile": true, "enterpriseProfileTemplatePath": "C:\\Sources\\wifi.xml" }
              },
              "unattend": { "isEnabled": false, "files": [ { "id": "0123456789abcdef0123456789abcdef", "displayName": "Answer", "sourcePath": "C:\\Sources\\answer.xml", "contentHash": "00" } ] }
            }
            """)!;

        LegacyFilePaths paths = FoundryConfigurationMigration.ExtractLegacyFilePaths(root);

        Assert.Equal(@"C:\Sources\wired.xml", paths.Dot1xProfileTemplate);
        Assert.Null(paths.Dot1xCertificate);
        Assert.Equal(@"C:\Sources\wifi.xml", paths.WifiEnterpriseProfileTemplate);
        Assert.Null(paths.WifiCertificate);
        Assert.Null(paths.AutopilotPfx);
        Assert.Equal(@"C:\Sources\answer.xml", Assert.Single(paths.UnattendSourcePaths).Value);
    }

    [Fact]
    public void Extract_Schema19DocumentWithCustomDrivers_KeepsSectionUnchanged()
    {
        JsonNode root = JsonNode.Parse(LegacyDocument(19))!;
        root["customDrivers"] = JsonNode.Parse("""
            { "isEnabled": true, "defaultSetId": "set-1", "sets": [ { "id": "set-1", "displayName": "Drivers", "isIncluded": true,
              "package": { "contentHash": "abc", "length": 12, "fileCount": 2 } } ] }
            """);
        string customDrivers = root["customDrivers"]!.ToJsonString();

        LegacyFilePaths paths = FoundryConfigurationMigration.ExtractLegacyFilePaths(root);

        Assert.Equal(@"C:\Sources\wired.xml", paths.Dot1xProfileTemplate);
        Assert.Equal(customDrivers, root["customDrivers"]!.ToJsonString());
    }

    [Fact]
    public void Extract_CurrentShape_ReturnsEmpty()
    {
        var service = new FoundryConfigurationService();
        FoundryConfigurationDocument current = new()
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = Reference("wired.xml") } },
            Unattend = new UnattendSettings { Files = [new UnattendFileSettings { Id = UnattendId, DisplayName = "Answer", ContentHash = new string('a', 64), File = Reference("answer.xml") }] }
        };
        JsonNode root = JsonNode.Parse(service.Serialize(current))!;
        string before = root.ToJsonString();

        LegacyFilePaths paths = FoundryConfigurationMigration.ExtractLegacyFilePaths(root);

        Assert.Null(paths.Dot1xProfileTemplate);
        Assert.Null(paths.Dot1xCertificate);
        Assert.Null(paths.WifiEnterpriseProfileTemplate);
        Assert.Null(paths.WifiCertificate);
        Assert.Null(paths.AutopilotPfx);
        Assert.Empty(paths.UnattendSourcePaths);
        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void Apply_ExistingFiles_SetsReferences()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "library"));
        string wired = CreateSource("wired.xml", "<LANProfile />");
        string certificate = CreateSource("wired.pfx", "pfx");
        string autopilot = CreateSource("autopilot.pfx", "autopilot");
        var paths = new LegacyFilePaths(wired, certificate, null, null, autopilot, new Dictionary<string, string>());

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyFilePaths(new FoundryConfigurationDocument(), paths, ImportExisting(library));

        AuthoringFileReference profile = Assert.IsType<AuthoringFileReference>(migrated.Network.Dot1x.ProfileTemplate);
        Assert.Equal("wired.xml", profile.FileName);
        Assert.Equal("<LANProfile />"u8.ToArray(), library.ReadVerified(profile));
        Assert.Equal("wired.pfx", migrated.Network.Dot1x.Certificate?.FileName);
        Assert.Equal("autopilot.pfx", migrated.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx?.FileName);
        Assert.Null(migrated.Network.Wifi.EnterpriseProfileTemplate);
        Assert.Null(migrated.Network.Wifi.Certificate);
    }

    [Fact]
    public void Apply_MissingFile_LeavesReferenceNull()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "library"));
        string missing = Path.Combine(temp.Path, "missing.xml");
        var paths = new LegacyFilePaths(missing, null, missing, missing, missing, new Dictionary<string, string>());

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyFilePaths(new FoundryConfigurationDocument(), paths, ImportExisting(library));

        Assert.Null(migrated.Network.Dot1x.ProfileTemplate);
        Assert.Null(migrated.Network.Wifi.EnterpriseProfileTemplate);
        Assert.Null(migrated.Network.Wifi.Certificate);
        Assert.Null(migrated.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "library", "content")));
    }

    [Fact]
    public void Apply_UnattendPath_MatchesById()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "library"));
        string answer = CreateSource("answer.xml", "<unattend />");
        string changed = CreateSource("changed.xml", "<unattend changed='true' />");
        string answerHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(answer)));
        const string otherId = "fedcba9876543210fedcba9876543210";
        const string missingId = "11111111111111111111111111111111";
        var document = new FoundryConfigurationDocument
        {
            Unattend = new UnattendSettings
            {
                Files =
                [
                    new UnattendFileSettings { Id = otherId, DisplayName = "Changed", ContentHash = new string('b', 64) },
                    new UnattendFileSettings { Id = UnattendId, DisplayName = "Answer", ContentHash = answerHash.ToUpperInvariant() },
                    new UnattendFileSettings { Id = missingId, DisplayName = "Missing", ContentHash = new string('c', 64) }
                ]
            }
        };
        var paths = new LegacyFilePaths(null, null, null, null, null, new Dictionary<string, string>
        {
            [UnattendId] = answer,
            [otherId] = changed,
            [missingId] = Path.Combine(temp.Path, "missing.xml")
        });

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyFilePaths(document, paths, ImportExisting(library));

        UnattendFileSettings matched = migrated.Unattend.Files.Single(file => file.Id == UnattendId);
        Assert.Equal(answerHash, matched.File?.ContentHash);
        Assert.Equal("answer.xml", matched.File?.FileName);
        // A source whose bytes no longer match the validated digest, or that is gone, stays listed and unavailable.
        UnattendFileSettings stale = migrated.Unattend.Files.Single(file => file.Id == otherId);
        Assert.Null(stale.File);
        Assert.Equal(new string('b', 64), stale.ContentHash);
        Assert.Null(migrated.Unattend.Files.Single(file => file.Id == missingId).File);
        Assert.Equal([otherId, UnattendId, missingId], migrated.Unattend.Files.Select(file => file.Id));
    }

    public void Dispose() => temp.Dispose();

    private static Func<string, AuthoringFileReference?> ImportExisting(AuthoringFileLibrary library) =>
        path => File.Exists(path) ? library.Import(File.ReadAllBytes(path), Path.GetFileName(path)) : null;

    private static AuthoringFileReference Reference(string fileName) =>
        new() { ContentHash = new string('a', 64), FileName = fileName, Length = 1 };

    private string CreateSource(string name, string content)
    {
        string path = Path.Combine(temp.Path, "sources", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string LegacyDocument(int schemaVersion) => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "network": {
            "dot1x": { "isEnabled": true, "profileTemplatePath": "C:\\Sources\\wired.xml", "requiresCertificate": true, "certificatePath": "C:\\Sources\\wired.pfx" },
            "wifiProvisioned": true,
            "wifi": { "isEnabled": true, "ssid": "Corp", "securityType": "WPA2/WPA3-Enterprise", "hasEnterpriseProfile": true,
              "enterpriseProfileTemplatePath": "C:\\Sources\\wifi.xml", "requiresCertificate": true, "certificatePath": "C:\\Sources\\wifi.cer" }
          },
          "autopilot": { "hardwareHashUpload": { "bootMediaCertificate": { "pfxPath": "C:\\Sources\\autopilot.pfx", "validatedThumbprint": "ABCDEF" } } },
          "unattend": { "isEnabled": true, "files": [ { "id": "{{UnattendId}}", "displayName": "Answer", "sourcePath": "C:\\Sources\\answer.xml", "contentHash": "00" } ] }
        }
        """;
}
