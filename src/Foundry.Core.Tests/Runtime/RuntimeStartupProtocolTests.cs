// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimeStartupProtocolTests
{
    [Theory]
    [InlineData(null, StartupCapabilityMode.Invalid)]
    [InlineData("{\"protocolVersions\":[1]}", StartupCapabilityMode.Supported)]
    [InlineData("{\"protocolVersions\":[2,1]}", StartupCapabilityMode.Supported)]
    [InlineData("{\"protocolVersions\":[2]}", StartupCapabilityMode.Incompatible)]
    [InlineData("{\"protocolVersions\":[]}", StartupCapabilityMode.Invalid)]
    [InlineData("{\"protocolVersions\":[0]}", StartupCapabilityMode.Invalid)]
    [InlineData("{\"protocolVersions\":[1,1]}", StartupCapabilityMode.Invalid)]
    [InlineData("{\"protocolVersions\":[\"1\"]}", StartupCapabilityMode.Invalid)]
    [InlineData("{\"protocolVersions\":[1],\"protocolVersions\":[2]}", StartupCapabilityMode.Invalid)]
    [InlineData("{", StartupCapabilityMode.Invalid)]
    public void CapabilityNegotiationAcceptsOnlyAValidAdvertisement(string? json, StartupCapabilityMode expected)
    {
        using var directory = new TemporaryDirectory();
        if (json is not null) File.WriteAllText(Path.Combine(directory.Path, StartupProtocol.ManifestFileName), json);
        StartupCapabilityResult result = RuntimeStartupCapabilities.Negotiate(Path.Combine(directory.Path, "Foundry.Connect.exe"));
        Assert.Equal(expected, result.Mode);
        Assert.Equal(expected == StartupCapabilityMode.Supported ? 1 : (int?)null, result.ProtocolVersion);
    }

    [Fact]
    public void OversizedManifestIsInvalid()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, StartupProtocol.ManifestFileName), new string(' ', StartupProtocol.MaximumFileBytes + 1));
        Assert.Equal(StartupCapabilityMode.Invalid,
            RuntimeStartupCapabilities.Negotiate(Path.Combine(directory.Path, "Foundry.Deploy.exe")).Mode);
    }

    [Fact]
    public void ReporterAtomicallyPublishesForwardStagesAndFailureCategory()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "launch", "status.json");
        string launch = Guid.NewGuid().ToString("N");
        RuntimeStartupReporter? reporter = CreateReporter(path, launch);
        Assert.NotNull(reporter);
        var reader = new RuntimeStartupStatusReader(path, "SESSION_1", launch, "Foundry.Connect", Environment.ProcessId);

        Assert.True(reporter.Report(StartupStage.ManagedStarted));
        Assert.Equal(StartupStage.ManagedStarted, reader.Read()?.Stage);
        Assert.False(reporter.Report(StartupStage.ManagedStarted));
        Assert.True(reporter.Report(StartupStage.ConfigurationLoaded));
        Assert.True(reporter.Report(StartupStage.UiReady));
        Assert.Equal(StartupStage.UiReady, reader.Read()?.Stage);
        Assert.Null(reader.Read());
        Assert.False(reporter.Report(StartupStage.ConfigurationLoaded));
        Assert.True(reporter.Report(StartupStage.StartupFailed, "startup_failed"));
        RuntimeStartupStatus? failed = reader.Read();
        Assert.Equal("startup_failed", failed?.FailureCategory);
        // Kept in the file shape for v26.10.18.1 compatibility, never populated.
        Assert.Null(failed?.FailureRecordId);
        Assert.Contains("\"failureRecordId\":null", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(reporter.Report(StartupStage.UiReady));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.True(new FileInfo(path).Length <= StartupProtocol.MaximumFileBytes);
    }

    [Fact]
    public void InvalidInterruptedAndWrongLaunchUpdatesDoNotReplaceAcknowledgement()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "status.json");
        RuntimeStartupStatus status = CreateStatus();
        var reader = new RuntimeStartupStatusReader(path, status.SessionId, status.LaunchId, status.Application, status.ProcessId);
        WriteStatus(path, status);
        Assert.NotNull(reader.Read());
        foreach (RuntimeStartupStatus invalid in new[]
        {
            status with { Stage = StartupStage.UiReady, LaunchId = Guid.NewGuid().ToString("N") },
            status with { Stage = StartupStage.UiReady, SessionId = "OTHER" },
            status with { Stage = StartupStage.UiReady, ProcessId = 43 },
            status with { Stage = StartupStage.UiReady, Application = "Foundry.Deploy" },
            status with { Stage = StartupStage.UiReady, ProtocolVersion = 2 },
            status with { Stage = StartupStage.UiReady, FailureCategory = "startup_failed" },
            status with { Stage = "unknown" }
        })
        {
            WriteStatus(path, invalid);
            Assert.Null(reader.Read());
            Assert.Equal(status, reader.LastStatus);
        }
        foreach (string malformed in new[] { "{", new string(' ', StartupProtocol.MaximumFileBytes + 1), "{}" })
        {
            File.WriteAllText(path, malformed);
            Assert.Null(reader.Read());
            Assert.Equal(status, reader.LastStatus);
        }
        WriteStatus(path, status with { Stage = StartupStage.UiReady, TimestampUtc = status.TimestampUtc.AddDays(-30) });
        Assert.Equal(StartupStage.UiReady, reader.Read()?.Stage);
        WriteStatus(path, status with { Stage = StartupStage.ConfigurationLoaded, TimestampUtc = status.TimestampUtc.AddDays(30) });
        Assert.Null(reader.Read());
    }

    [Theory]
    [InlineData("protocolVersion")]
    [InlineData("stage")]
    public void DuplicateStatusPropertiesAreRejected(string propertyName)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "status.json");
        string json = JsonSerializer.Serialize(CreateStatus(), RuntimeStartupFile.JsonOptions);
        string duplicateValue = propertyName == "stage" ? "\"ui_ready\"" : "1";
        File.WriteAllText(path, json[..^1] + ",\"" + propertyName + "\":" + duplicateValue + "}");
        Assert.Null(RuntimeStartupStatusReader.ReadValidated(path));
    }

    [Fact]
    public void MissingOrInvalidEnvironmentLeavesStandaloneStartupUnchanged()
    {
        Assert.Null(RuntimeStartupReporter.FromEnvironment("Foundry.Connect", _ => null));
        Assert.Null(CreateReporter("relative.json", Guid.NewGuid().ToString("N")));
        using var directory = new TemporaryDirectory();
        Assert.Null(CreateReporter(Path.Combine(directory.Path, "status.json"), "wrong-launch"));
        Assert.Null(CreateReporter(Path.Combine(directory.Path, "status.json"), Guid.Empty.ToString("N")));
    }

    [Theory]
    [InlineData("Foundry.WinPE", true)]
    [InlineData("Foundry.Connect", true)]
    [InlineData("Foundry.Deploy", true)]
    [InlineData("Foundry.Unknown", false)]
    public void OnlyKnownApplicationsReportStartup(string application, bool accepted)
    {
        using var directory = new TemporaryDirectory();
        RuntimeStartupReporter? reporter = RuntimeStartupReporter.FromEnvironment(application, name => name switch
        {
            StartupProtocol.ProtocolEnvironmentVariable => "1",
            StartupProtocol.LaunchIdEnvironmentVariable => Guid.NewGuid().ToString("N"),
            StartupProtocol.SessionIdEnvironmentVariable => "SESSION_1",
            StartupProtocol.StatusPathEnvironmentVariable => Path.Combine(directory.Path, "status.json"),
            _ => null
        });

        Assert.Equal(accepted, reporter is not null);
    }

    [Fact]
    public void FailedStatusWriteDoesNotThrowOrCommitStage()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "status.json");
        RuntimeStartupReporter reporter = CreateReporter(path, Guid.NewGuid().ToString("N"))!;
        Directory.CreateDirectory(path);
        Assert.False(reporter.Report(StartupStage.ManagedStarted));
        Directory.Delete(path);
        Assert.True(reporter.Report(StartupStage.ManagedStarted));
    }

    private static RuntimeStartupReporter? CreateReporter(string path, string launchId) =>
        RuntimeStartupReporter.FromEnvironment("Foundry.Connect", name => name switch
        {
            StartupProtocol.ProtocolEnvironmentVariable => "1",
            StartupProtocol.LaunchIdEnvironmentVariable => launchId,
            StartupProtocol.SessionIdEnvironmentVariable => "SESSION_1",
            StartupProtocol.StatusPathEnvironmentVariable => path,
            _ => null
        });

    private static RuntimeStartupStatus CreateStatus() => new()
    {
        ProtocolVersion = StartupProtocol.Version,
        SessionId = "SESSION_1",
        LaunchId = Guid.NewGuid().ToString("N"),
        Application = "Foundry.Connect",
        ProcessId = 42,
        Stage = StartupStage.ManagedStarted,
        TimestampUtc = DateTimeOffset.UtcNow
    };

    private static void WriteStatus(string path, RuntimeStartupStatus status) =>
        File.WriteAllText(path, JsonSerializer.Serialize(status, RuntimeStartupFile.JsonOptions));
}
