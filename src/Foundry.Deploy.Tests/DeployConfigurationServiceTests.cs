// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Runtime;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeployConfigurationServiceTests
{
    private static readonly BootMediaRuntimeContext ProductionRuntime = new("26.10.3.2", true, false, false);

    [Fact]
    public void LoadOptional_DoesNotRecommendRebuildWithoutConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance,
            System.IO.Path.Combine(directory.Path, "missing.json"), ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.False(result.Exists);
        Assert.Null(result.Document);
        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(", \"uploadComputerNameToAutopilot\": true", true)]
    public void LoadOptional_ReadsComputerNameUploadPreferenceWithoutEnablingOldMedia(string property, bool expected)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json",
            "{\"schemaVersion\":13,\"customization\":{\"machineNaming\":{\"isEnabled\":true" + property + "}}}");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.Equal(expected, result.Document.Customization.MachineNaming.UploadComputerNameToAutopilot);
    }

    [Fact]
    public void LoadOptional_PreservesParseExceptionForStartupDiagnostics()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "invalid.json", "{invalid}");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Null(result.Document);
        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
        var exception = Assert.IsType<System.Text.Json.JsonException>(result.FailureException);
        Assert.NotEmpty(exception.StackTrace!);
        Assert.Equal(exception.Message, result.FailureMessage);
    }

    [Theory]
    [InlineData("{\"isEnabled\":true,\"files\":[]}")]
    [InlineData("{\"isEnabled\":true,\"defaultFileId\":\"missing\",\"files\":[]}")]
    public void LoadOptional_WhenUnattendManifestIsInvalid_FailsClosed(string manifest)
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = CreateJsonFile(tempDirectory.Path, "foundry.deploy.config.json", "{\"unattend\":" + manifest + "}");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Null(result.Document);
        Assert.NotEmpty(result.FailureMessage!);
    }

    [Fact]
    public void LoadOptional_WhenRemoteDiagnosticsConsentIsMissing_DefaultsToEnabled()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            """{ "schemaVersion": 11, "telemetry": { "isEnabled": true } }""");
        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Telemetry.IsRemoteDiagnosticsEnabled);
    }

    [Fact]
    public void LoadOptional_RecommendsRebuildForOlderAuthoringReleaseWithCurrentSchema()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{FoundryDeployConfigurationDocument.CurrentSchemaVersion}},"authoringVersion":"26.10.3.1"}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.NewerRelease, result.BootMediaUpdateReason);
    }

    [Fact]
    public void LoadOptional_UsesLegacyFallbackWithoutAuthoringMetadata()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":15}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.UnknownAuthoringVersion, result.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("26.10.3.2", false, false, false, "release")]
    [InlineData("26.10.3.2", true, true, false, "release")]
    [InlineData("26.10.3.2", true, false, true, "release")]
    [InlineData("1.0.0.0", true, false, false, "release")]
    [InlineData("26.10.3.2", true, false, false, " Debug ")]
    public void LoadOptional_SuppressesAdviceOutsideProductionWinPe(string version, bool winPe, bool debugger, bool debugBuild, string source)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":15,"authoringVersion":"26.10.3.1"}""");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "foundry.deploy.provisioning-source.txt"), source);
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, new BootMediaRuntimeContext(version, winPe, debugger, debugBuild));

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("debug", BootMediaUpdateReason.None)]
    [InlineData("unknown", BootMediaUpdateReason.UnknownAuthoringVersion)]
    public void LoadOptional_OnlyExplicitDebugTelemetrySuppressesLegacyAdvice(string source, BootMediaUpdateReason expected)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$$"""{"telemetry":{"runtimePayloadSource":"{{{source}}}"}}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(expected, result.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("26.10.3.2")]
    [InlineData("26.10.4.1")]
    public void LoadOptional_DoesNotRecommendRebuildForSameOrNewerAuthor(string author)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":15,"authoringVersion":"{{author}}"}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
    }

    [Fact]
    public void LoadOptional_UsesLegacyFallbackWhenProvisioningMarkerIsUnreadable()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":15}""");
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "foundry.deploy.provisioning-source.txt"));
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.UnknownAuthoringVersion, result.BootMediaUpdateReason);
    }

    [Fact]
    public void LoadOptional_WhenCompletionSettingsAreMissing_UsesAutomaticTenSecondReboot()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": {{FoundryDeployConfigurationDocument.CurrentSchemaVersion}}
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Completion.AutomaticRebootEnabled);
        Assert.Equal(10, result.Document.Completion.AutomaticRebootDelaySeconds);
    }

    [Fact]
    public void LoadOptional_WhenLegacyMachineNamingUsesGeneratedSuffix_MigratesToComposition()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            """
            {
              "schemaVersion": 11,
              "customization": {
                "machineNaming": {
                  "isEnabled": true,
                  "prefix": "LAB-",
                  "autoGenerateName": true,
                  "allowManualSuffixEdit": false
                }
              }
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        DeployMachineNamingSettings naming = Assert.IsType<FoundryDeployConfigurationDocument>(result.Document)
            .Customization.MachineNaming;
        Assert.Equal(Foundry.Core.Models.Configuration.MachineNamingMode.Composed, naming.Mode);
        Assert.False(naming.AllowEditingDuringDeployment);
        Assert.Collection(
            naming.Components,
            component =>
            {
                Assert.Equal(Foundry.Core.Models.Configuration.MachineNameComponentType.StaticText, component.Type);
                Assert.Equal("LAB-", component.StaticText);
            },
            component =>
            {
                Assert.Equal(Foundry.Core.Models.Configuration.MachineNameComponentType.Random, component.Type);
                Assert.Equal(6, component.MaximumLength);
            });
    }

    [Fact]
    public void LoadOptional_WhenLegacyConfigurationContainsNetworkProfileRoaming_MigratesBothTransports()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            $$"""
            {
              "schemaVersion": 11,
              "network": {
                "profileRoaming": {
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(result.Document.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.True(result.Document.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.True(result.Document.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void LoadOptional_WhenSplitRoamingSettingsArePartial_BackfillsOnlyMissingFieldsFromLegacySettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            """
            {
              "schemaVersion": 12,
              "network": {
                "profileRoaming": {
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true,
                  "wiredDot1x": {
                    "isEnabled": true
                  },
                  "wifi": {
                    "includePrivateKeyMaterial": false
                  }
                }
              }
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(result.Document.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.True(result.Document.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.False(result.Document.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void LoadOptional_WhenSplitRoamingSettingsAreExplicit_DoesNotLetLegacyFallbackOverrideThem()
    {
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.deploy.config.json",
            """
            {
              "schemaVersion": 12,
              "network": {
                "profileRoaming": {
                  "wiredDot1x": {
                    "isEnabled": false,
                    "includePrivateKeyMaterial": false
                  },
                  "wifi": {
                    "isEnabled": false,
                    "includePrivateKeyMaterial": false
                  },
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.False(result.Document.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.False(result.Document.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.False(result.Document.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.False(result.Document.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    private static string CreateJsonFile(string directoryPath, string fileName, string contents)
    {
        string filePath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
