// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.WinPE.ObsoleteMedia;
using Xunit;

namespace Foundry.WinPE.Tests.ObsoleteMedia;

/// <summary>Sets the process-wide startup protocol variables, so these tests never run beside another test.</summary>
[CollectionDefinition(nameof(StartupProtocolEnvironmentCollection), DisableParallelization = true)]
public sealed class StartupProtocolEnvironmentCollection;

[Collection(nameof(StartupProtocolEnvironmentCollection))]
public sealed class ObsoleteMediaModeTests
{
    private const string SessionId = "SESSION_1";

    [Theory]
    [InlineData(@"X:\Foundry\Runtime\Foundry.Connect\Foundry.Connect.exe", "Foundry.Connect")]
    [InlineData(@"X:\Foundry\Runtime\Foundry.Deploy\foundry.deploy.EXE", "Foundry.Deploy")]
    public void TryGetApplicationId_RecognizesLegacyExecutableNames(string processPath, string expected)
    {
        Assert.True(ObsoleteMediaMode.TryGetApplicationId(processPath, [], out string? applicationId));
        Assert.Equal(expected, applicationId);
    }

    [Fact]
    public void TryGetApplicationId_RecognizesLegacyLauncherConfigArguments()
    {
        string[] args = ["--config", @"X:\Foundry\Config\foundry.connect.config.json"];
        Assert.True(ObsoleteMediaMode.TryGetApplicationId(@"X:\Foundry\Runtime\Foundry.Connect\Foundry.Connect.exe", args, out string? applicationId));
        Assert.Equal("Foundry.Connect", applicationId);
    }

    [Theory]
    [InlineData(@"X:\Foundry\Foundry.WinPE.exe", "")]
    [InlineData(@"X:\Foundry\Foundry.WinPE.exe", "--ui")]
    [InlineData(@"X:\Foundry\Foundry.WinPE.exe", "--native-deployment-worker")]
    [InlineData(@"X:\Foundry\Foundry.Deploy.exe", "--native-deployment-worker")]
    [InlineData(@"X:\Foundry\Foundry.Connect.exe", "--ui --native-deployment-worker")]
    [InlineData(null, "")]
    public void TryGetApplicationId_IgnoresCurrentExecutableAndTheNativeWorker(string? processPath, string arguments)
    {
        string[] args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.False(ObsoleteMediaMode.TryGetApplicationId(processPath, args, out string? applicationId));
        Assert.Null(applicationId);
    }

    [Theory]
    [InlineData("Foundry.Connect")]
    [InlineData("Foundry.Deploy")]
    public void Run_ReportsManagedStartedThenUiReadyAndReturnsUserAborted(string applicationId)
    {
        using var environment = new StartupEnvironment();
        var reader = new RuntimeStartupStatusReader(
            environment.StatusPath, SessionId, environment.LaunchId, applicationId, Environment.ProcessId);
        string? stageBeforeRendering = null;

        int exitCode = ObsoleteMediaMode.Run(applicationId, onRendered =>
        {
            stageBeforeRendering = reader.Read()?.Stage;
            onRendered();
        });

        Assert.Equal(StartupStage.ManagedStarted, stageBeforeRendering);
        RuntimeStartupStatus? status = reader.Read();
        Assert.Equal(StartupStage.UiReady, status?.Stage);
        Assert.Null(status?.FailureCategory);
        Assert.Equal((int)ChildRuntimeExitCode.UserAborted, exitCode);
    }

    [Fact]
    public void Run_WithoutStartupEnvironment_ShowsTheWindowWithoutWritingStatus()
    {
        using var environment = new StartupEnvironment(publish: false);
        bool shown = false;

        int exitCode = ObsoleteMediaMode.Run("Foundry.Deploy", onRendered =>
        {
            shown = true;
            onRendered();
        });

        Assert.True(shown);
        Assert.False(File.Exists(environment.StatusPath));
        Assert.Equal((int)ChildRuntimeExitCode.UserAborted, exitCode);
    }

    [Fact]
    public void Run_WhenTheWindowFails_NeverReportsStartupFailed()
    {
        using var environment = new StartupEnvironment();
        var reader = new RuntimeStartupStatusReader(
            environment.StatusPath, SessionId, environment.LaunchId, "Foundry.Connect", Environment.ProcessId);

        Assert.Throws<InvalidOperationException>(() =>
            ObsoleteMediaMode.Run("Foundry.Connect", _ => throw new InvalidOperationException("window")));

        Assert.Equal(StartupStage.ManagedStarted, reader.Read()?.Stage);
    }

    /// <summary>Publishes, then restores, the variables a legacy launcher sets for its child.</summary>
    private sealed class StartupEnvironment : IDisposable
    {
        private static readonly string[] Names =
        [
            StartupProtocol.ProtocolEnvironmentVariable,
            StartupProtocol.StatusPathEnvironmentVariable,
            StartupProtocol.SessionIdEnvironmentVariable,
            StartupProtocol.LaunchIdEnvironmentVariable
        ];

        private readonly string _directory = Directory.CreateTempSubdirectory("FoundryObsoleteMedia-").FullName;
        private readonly Dictionary<string, string?> _previous = Names.ToDictionary(name => name, Environment.GetEnvironmentVariable);

        public StartupEnvironment(bool publish = true)
        {
            StatusPath = Path.Combine(_directory, "status.json");
            LaunchId = Guid.NewGuid().ToString("N");
            Set(StartupProtocol.ProtocolEnvironmentVariable, publish ? "1" : null);
            Set(StartupProtocol.StatusPathEnvironmentVariable, publish ? StatusPath : null);
            Set(StartupProtocol.SessionIdEnvironmentVariable, publish ? SessionId : null);
            Set(StartupProtocol.LaunchIdEnvironmentVariable, publish ? LaunchId : null);
        }

        public string StatusPath { get; }

        public string LaunchId { get; }

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous) Set(name, value);
            Directory.Delete(_directory, recursive: true);
        }

        private static void Set(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
    }
}
