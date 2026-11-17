// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Core.Services.Runtime;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using ConfigurationSchemaVersions = Foundry.Core.Models.Configuration.ConfigurationSchemaVersions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeployConfigurationServiceTests
{
    [Theory]
    [InlineData("""{"isEnabled":true,"mode":1,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test","accountName":"CORP\\join","encryptedCredentials":{}}]}""")]
    [InlineData("""{"isEnabled":true,"mode":1,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test","accountName":"CORP\\join"}]}""")]
    [InlineData("""{"isEnabled":true,"mode":0,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test","encryptedCredentials":{}}]}""")]
    [InlineData("""{"isEnabled":true,"mode":0,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test","accountName":"CORP\\join"}]}""")]
    [InlineData("""{"isEnabled":true,"mode":0,"defaultDomainId":"a","domains":[{"id":"a","domainName":"invalid domain"}]}""")]
    [InlineData("""{"isEnabled":true,"mode":0,"defaultDomainId":"missing","domains":[{"id":"a","domainName":"corp.test"}]}""")]
    [InlineData("""{"isEnabled":true,"mode":0,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test","organizationalUnits":null}]}""")]
    [InlineData("""{"isEnabled":true,"domains":null}""")]
    [InlineData("""{"isEnabled":true,"domains":[null]}""")]
    public void LoadOptional_RejectsUnprotectedAutomaticOrMalformedDomainMetadata(string domain)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", WinPe("{\"domainJoin\":" + domain + "}"));
        DeployConfigurationLoadResult result = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();
        Assert.Null(result.Document);
        Assert.NotEmpty(result.FailureMessage!);
        Assert.DoesNotContain("CORP", result.FailureMessage);
    }

    [Fact]
    public void LoadOptional_ReadsTheSharedSectionsAndTheDeploySectionOnly()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json", """
            {
              // Hand-written casing and comments are accepted.
              "SchemaVersion": 1,
              "AuthoringVersion": "26.11.1.1",
              "Telemetry": { "installId": "install-id" },
              "Protection": { "isEnabled": false },
              "Network": { "profileRoaming": { "wiredDot1x": { "isEnabled": true } }, "artifactRootPath": "Y:/Roaming" },
              "Connect": { "wifi": "not a Deploy concern" },
              "Deploy": { "completion": { "automaticRebootDelaySeconds": 42 } },
              "operatingSystemSelection": "ignored: not inside the deploy section"
            }
            """);

        DeployConfigurationLoadResult result = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.NotNull(result.Document);
        Assert.Equal("26.11.1.1", result.Document.AuthoringVersion);
        Assert.Equal("install-id", result.Document.Telemetry.InstallId);
        Assert.True(result.Document.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.Equal("Y:/Roaming", result.Document.Network.ArtifactRootPath);
        Assert.Equal(42, result.Document.Completion.AutomaticRebootDelaySeconds);
        Assert.False(result.Document.Protection.IsEnabled);
    }

    [Fact]
    public void LoadOptional_IgnoresDeploySettingsWrittenAtTheRoot()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json",
            """{"schemaVersion":1,"completion":{"automaticRebootDelaySeconds":42}}""");

        DeployConfigurationLoadResult result = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.Equal(10, result.Document!.Completion.AutomaticRebootDelaySeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LoadOptional_InvalidSchema_FailsClosed(int schemaVersion)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json", "{\"schemaVersion\":" + schemaVersion + "}");

        DeployConfigurationLoadResult result = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.True(result.Exists);
        Assert.Null(result.Document);
        Assert.NotEmpty(result.FailureMessage!);
    }

    [Fact]
    public void LoadOptional_NewerSchema_LoadsWithAWarning()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json",
            $$"""{"schemaVersion":{{ConfigurationSchemaVersions.WinPeCurrent + 1}}}""");
        var logger = new WarningCountingLogger();

        DeployConfigurationLoadResult result = new DeployConfigurationService(logger, path).LoadOptional();

        Assert.NotNull(result.Document);
        Assert.Equal(1, logger.WarningCount);
    }

    [Theory]
    [InlineData("""{"id":"b","domainName":"emea.test","accountName":"EMEA\\join","encryptedCredentials":{}}""", true)]
    [InlineData("""{"id":"b","domainName":"emea.test","accountName":"EMEA\\join"}""", false)]
    [InlineData("""{"id":"b","domainName":"emea.test","encryptedCredentials":{}}""", false)]
    // The account already has a payload on the first domain, so only the per-domain rule can refuse this one.
    [InlineData("""{"id":"b","domainName":"emea.test","accountName":"CORP\\join"}""", false)]
    public void LoadOptional_RequiresCredentialsForEveryZeroTouchDomainOnProtectedMedia(string second, bool loads)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json",
            """{"protection":{"isEnabled":true},"deploy":{"domainJoin":{"isEnabled":true,"mode":1,"defaultDomainId":"a","domains":[""" +
            """{"id":"a","domainName":"corp.test","accountName":"CORP\\join","encryptedCredentials":{}},""" + second + "]}}}");

        DeployConfigurationLoadResult result = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.Equal(loads, result.Document is not null);
    }

    [Fact]
    public void LoadOptional_PreservesInteractiveSettingsOnUnprotectedMedia()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json",
            WinPe("""{"domainJoin":{"isEnabled":true,"mode":0,"defaultDomainId":"a","domains":[{"id":"a","domainName":"corp.test"}]}}"""));
        DeployConfigurationLoadResult result = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();
        Assert.NotNull(result.Document);
        Assert.True(result.Document.DomainJoin.IsEnabled);
        var domain = Assert.Single(result.Document.DomainJoin.Domains);
        Assert.Equal("corp.test", domain.DomainName);
        Assert.Null(domain.AccountName);
        Assert.Null(domain.EncryptedCredentials);
        Assert.False(result.Document.Protection.IsEnabled);
    }

    [Fact]
    public void LoadOptional_RejectsConflictingDomainAndAutopilotBeforeLaunch()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json",
            WinPe("""{"domainJoin":{"isEnabled":true},"autopilot":{"isEnabled":true}}"""));
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Null(result.Document);
        Assert.NotEmpty(result.FailureMessage!);
    }

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
            WinPe("{\"customization\":{\"machineNaming\":{\"isEnabled\":true" + property + "}}}"));
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
        var exception = Assert.IsAssignableFrom<System.Text.Json.JsonException>(result.FailureException);
        Assert.NotEmpty(exception.StackTrace!);
        Assert.Equal(exception.Message, result.FailureMessage);
    }

    [Theory]
    [InlineData("{\"isEnabled\":true,\"files\":[]}")]
    [InlineData("{\"isEnabled\":true,\"defaultFileId\":\"missing\",\"files\":[]}")]
    public void LoadOptional_WhenUnattendManifestIsInvalid_FailsClosed(string manifest)
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = CreateJsonFile(tempDirectory.Path, "foundry.winpe.config.json", WinPe("{\"unattend\":" + manifest + "}"));
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
            "foundry.winpe.config.json",
            """{ "schemaVersion": 1, "telemetry": { "isEnabled": true } }""");
        var service = new DeployConfigurationService(
            NullLogger<DeployConfigurationService>.Instance,
            configurationPath);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.NotNull(result.Document);
        Assert.True(result.Document.Telemetry.IsRemoteDiagnosticsEnabled);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"telemetry":null}""")]
    public void LoadOptional_WhenTelemetryIsMissingOrNull_DisablesTelemetry(string json)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json", json);

        DeployConfigurationLoadResult result = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.NotNull(result.Document);
        Assert.False(result.Document.Telemetry.IsEnabled);
        Assert.False(result.Document.Telemetry.IsRemoteDiagnosticsEnabled);
    }

    [Fact]
    public void LoadOptional_RecommendsRebuildForOlderAuthoringReleaseWithCurrentSchema()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{ConfigurationSchemaVersions.WinPeCurrent}},"authoringVersion":"26.10.3.1"}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.NewerRelease, result.BootMediaUpdateReason);
        Assert.Null(result.LatestReleaseCheck);
    }

    [Fact]
    public void LoadOptional_UsesLegacyFallbackWithoutAuthoringMetadata()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1}""");
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
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1,"authoringVersion":"26.10.3.1"}""");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, WinPeWorkspaceLayout.ProvisioningSourceFileName), source);
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, new BootMediaRuntimeContext(version, winPe, debugger, debugBuild));

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
        Assert.Null(result.LatestReleaseCheck);
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
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":1,"authoringVersion":"{{author}}"}""");
        var service = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path, ProductionRuntime);

        DeployConfigurationLoadResult result = service.LoadOptional();

        Assert.Equal(BootMediaUpdateReason.None, result.BootMediaUpdateReason);
        // Only the latest published release can still reveal newer boot media.
        Assert.Equal(new BootMediaLatestReleaseCheck(author, ProductionRuntime.RuntimeVersion!), result.LatestReleaseCheck);
    }

    [Fact]
    public void LoadOptional_UsesLegacyFallbackWhenProvisioningMarkerIsUnreadable()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1}""");
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, WinPeWorkspaceLayout.ProvisioningSourceFileName));
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
            "foundry.winpe.config.json",
            $$"""
            {
              "schemaVersion": {{ConfigurationSchemaVersions.WinPeCurrent}}
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
    public void LoadOptional_ReadsTheProtectionKeyCheck()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json",
            """{"protection":{"isEnabled":true,"keyCheck":{"kind":"encrypted","algorithm":"aes-gcm-v1","keyId":"deployment-key-check","nonce":"n","tag":"t","ciphertext":"c"}}}""");

        DeployConfigurationLoadResult result = new DeployConfigurationService(NullLogger<DeployConfigurationService>.Instance, path).LoadOptional();

        Assert.Equal("deployment-key-check", result.Document!.Protection.KeyCheck!.KeyId);
    }

    // A WinPE runtime configuration whose deploy section is the given JSON object.
    private static string WinPe(string deploySection) => "{\"schemaVersion\":1,\"deploy\":" + deploySection + "}";

    private sealed class WarningCountingLogger : Microsoft.Extensions.Logging.ILogger<DeployConfigurationService>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                WarningCount++;
            }
        }
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
