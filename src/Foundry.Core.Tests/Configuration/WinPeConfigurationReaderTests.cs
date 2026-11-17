// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Security;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class WinPeConfigurationReaderTests
{
    [Theory]
    [InlineData(int.MinValue, WinPeConfigurationSchemaDecision.Invalid)]
    [InlineData(0, WinPeConfigurationSchemaDecision.Invalid)]
    [InlineData(1, WinPeConfigurationSchemaDecision.Current)]
    [InlineData(2, WinPeConfigurationSchemaDecision.Newer)]
    public void Evaluate_ClassifiesTheSchemaVersion(int schemaVersion, WinPeConfigurationSchemaDecision expected) =>
        Assert.Equal(expected, WinPeConfigurationSchemaPolicy.Evaluate(schemaVersion));

    [Fact]
    public void ReadModeView_FlattensTheSharedSectionsAndTheModeSectionOnly()
    {
        string json = WinPeConfigurationTestDocuments.Json(new DeployRuntimeSettings { Completion = new() { AutomaticRebootDelaySeconds = 42 } });

        JsonObject deploy = WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.DeploySection);
        JsonObject connect = WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.ConnectSection);

        Assert.Equal(42, deploy["completion"]!["automaticRebootDelaySeconds"]!.GetValue<int>());
        Assert.Equal("26.11.1.1", deploy["authoringVersion"]!.GetValue<string>());
        Assert.True(deploy["protection"]!["isEnabled"]!.GetValue<bool>());
        Assert.False(deploy.ContainsKey("wifi"));
        Assert.False(deploy.ContainsKey("connect"));
        Assert.Equal("Corp WiFi", connect["wifi"]!["ssid"]!.GetValue<string>());
        Assert.Equal(@"X:\Foundry\Runtime\Roaming", connect["network"]!["artifactRootPath"]!.GetValue<string>());
        Assert.False(connect.ContainsKey("completion"));
        Assert.False(connect.ContainsKey("deploy"));
    }

    [Fact]
    public void ReadModeView_FindsSectionsWhateverTheirCasing()
    {
        const string json = """
            {
              // A hand-written file.
              "SchemaVersion": 1,
              "TELEMETRY": { "isEnabled": true },
              "Protection": { "isEnabled": true },
              "Network": { "artifactRootPath": "Y:\\Roaming" },
              "Connect": { "Wifi": { "ssid": "Lab" } },
            }
            """;

        JsonObject view = WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.ConnectSection);
        ConnectRuntimeSettings connect = view.Deserialize<ConnectRuntimeSettings>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(1, view["schemaVersion"]!.GetValue<int>());
        Assert.True(view["telemetry"]!["isEnabled"]!.GetValue<bool>());
        Assert.True(view["protection"]!["isEnabled"]!.GetValue<bool>());
        Assert.Equal(@"Y:\Roaming", view["network"]!["artifactRootPath"]!.GetValue<string>());
        Assert.Equal("Lab", connect.Wifi.Ssid);
    }

    [Fact]
    public void ReadModeView_KeepsTheRootSectionWhenTheModeSectionRepeatsIt()
    {
        const string json = """{"protection":{"isEnabled":true},"deploy":{"protection":{"isEnabled":false},"completion":{}}}""";

        JsonObject view = WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.DeploySection);

        Assert.True(view["protection"]!["isEnabled"]!.GetValue<bool>());
        Assert.True(view.ContainsKey("completion"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"deploy":[]}""")]
    [InlineData("""{"deploy":{},"Deploy":{}}""")]
    [InlineData("""{"deploy":{"completion":{},"Completion":{}}}""")]
    [InlineData("{invalid}")]
    public void ReadModeView_RejectsADocumentThatIsNotAWinPeConfiguration(string json) =>
        Assert.ThrowsAny<JsonException>(() => WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.DeploySection));

    [Theory]
    [InlineData(WinPeConfigurationReader.ConnectSection, WinPeConfigurationReader.DeploySection, "5")]
    [InlineData(WinPeConfigurationReader.ConnectSection, WinPeConfigurationReader.DeploySection, "\"x\"")]
    [InlineData(WinPeConfigurationReader.ConnectSection, WinPeConfigurationReader.DeploySection, """{"completion":{"automaticRebootDelaySeconds":"x"}}""")]
    [InlineData(WinPeConfigurationReader.DeploySection, WinPeConfigurationReader.ConnectSection, "5")]
    [InlineData(WinPeConfigurationReader.DeploySection, WinPeConfigurationReader.ConnectSection, "\"x\"")]
    [InlineData(WinPeConfigurationReader.DeploySection, WinPeConfigurationReader.ConnectSection, """{"wifi":{"ssid":5}}""")]
    public void ReadModeView_IgnoresAMalformedSectionOfTheOtherMode(string mode, string otherMode, string otherSection)
    {
        string modeSection = mode == WinPeConfigurationReader.ConnectSection
            ? """{"wifi":{"ssid":"Lab"}}"""
            : """{"completion":{"automaticRebootDelaySeconds":42}}""";
        string json = $$"""{"schemaVersion":1,"{{mode}}":{{modeSection}},"{{otherMode}}":{{otherSection}}}""";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        JsonObject view = WinPeConfigurationReader.ReadModeView(json, mode);

        Assert.False(view.ContainsKey(otherMode));
        if (mode == WinPeConfigurationReader.ConnectSection)
        {
            Assert.Equal("Lab", view.Deserialize<ConnectRuntimeSettings>(options)!.Wifi.Ssid);
        }
        else
        {
            Assert.Equal(42, view.Deserialize<DeployRuntimeSettings>(options)!.Completion.AutomaticRebootDelaySeconds);
        }
    }

    [Fact]
    public void ReadModeView_WithoutTheModeSection_HasOnlyTheSharedSections()
    {
        JsonObject view = WinPeConfigurationReader.ReadModeView("""{"schemaVersion":1,"deploy":null}""", WinPeConfigurationReader.DeploySection);

        Assert.Equal(["schemaVersion"], view.Select(property => property.Key));
    }

    [Fact]
    public void GeneratedProtection_UnlocksWithItsPasswordOnly()
    {
        const string password = "correct horse battery staple";
        using var directory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = DeploymentMediaProtectionService.CreateProtected(password);
        string json = new WinPeConfigurationGenerator().CreateBundle(
            new FoundryConfigurationDocument(), MediaInputs.None, directory.Path, protection).ConfigurationJson;

        DeployProtectionSettings read = WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.DeploySection)["protection"]
            .Deserialize<DeployProtectionSettings>(ConfigurationJsonDefaults.SerializerOptions)!;
        byte[]? key = DeploymentKeyProtection.TryUnwrap(read, password);

        Assert.True(read.IsEnabled);
        Assert.NotNull(key);
        Assert.Equal(protection.DeploymentKey.ToArray(), key);
        Assert.True(DeploymentKeyProtection.VerifyKeyCheck(read.KeyCheck, key));
        Assert.Null(DeploymentKeyProtection.TryUnwrap(read, "wrong password"));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(read.KeyCheck, new byte[key.Length]));
    }
}
