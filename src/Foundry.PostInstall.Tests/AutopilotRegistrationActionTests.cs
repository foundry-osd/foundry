// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class AutopilotRegistrationActionTests : IDisposable
{
    private const string ExecutablePath = "Runtime/AutopilotInteractive/Foundry.AutopilotInteractive.exe";
    private const string ConfigPath = "Runtime/AutopilotInteractive/autopilot-interactive.json";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 30, 15, 250, TimeSpan.Zero);
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));

    public AutopilotRegistrationActionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Runtime", "AutopilotInteractive"));
        File.WriteAllText(Path.Combine(root, ExecutablePath), "fixture");
        File.WriteAllText(Path.Combine(root, ConfigPath), "{}");
    }

    [Fact]
    public async Task Execute_RunsExecutableWithConfigAndDeadline()
    {
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes);

        Assert.True(outcome.Succeeded);
        ProcessCommand command = Assert.Single(processes.Commands);
        string executable = Path.Combine(root, "Runtime", "AutopilotInteractive", "Foundry.AutopilotInteractive.exe");
        Assert.Equal(executable, command.FileName);
        Assert.Equal(Path.GetDirectoryName(executable), command.WorkingDirectory);
        Assert.Equal(
            ["--config", Path.Combine(root, "Runtime", "AutopilotInteractive", "autopilot-interactive.json"),
             "--deadline", (Now + AutopilotRegistrationAction.Timeout).ToString("O", CultureInfo.InvariantCulture)],
            command.Arguments);
        Assert.Equal("2026-11-01T10:30:15.2500000+00:00", command.Arguments[3]);
        Assert.Equal(AutopilotRegistrationAction.Timeout, command.Timeout);
        Assert.Equal(Path.Combine(root, "Logs", "PreOobe", "autopilot-registration", "Process", "output.log"), command.OutputPath);
    }

    [Fact]
    public async Task Execute_ReturnsAssistantMissing_WhenExecutableAbsent()
    {
        File.Delete(Path.Combine(root, ExecutablePath));
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes);

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_assistant_missing", outcome.FailureCode);
        Assert.Empty(processes.Commands);
    }

    [Fact]
    public async Task Execute_MapsExitCodes()
    {
        ActionStepOutcome completed = await Execute(new RecordingProcesses(new(0, "")));
        ActionStepOutcome incomplete = await Execute(new RecordingProcesses(new(1, "")));
        ActionStepOutcome unknown = await Execute(new RecordingProcesses(new(3, "")));

        Assert.True(completed.Succeeded);
        Assert.Null(completed.FailureCode);
        Assert.False(completed.HasWarnings);
        Assert.False(incomplete.Succeeded);
        Assert.Equal(1, incomplete.ExitCode);
        Assert.Equal("autopilot_registration_incomplete", incomplete.FailureCode);
        Assert.False(incomplete.TerminationUncertain);
        Assert.False(incomplete.HasWarnings);
        Assert.False(unknown.Succeeded);
        Assert.Equal(3, unknown.ExitCode);
        Assert.Equal("autopilot_registration_incomplete", unknown.FailureCode);
    }

    [Fact]
    public async Task Execute_ReportsSkippedRegistrationAsSucceededWithWarning()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(2, "")));

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Equal("autopilot_registration_skipped", outcome.FailureCode);
        Assert.True(outcome.HasWarnings);
        Assert.False(outcome.TerminationUncertain);
        Assert.False(outcome.RestartRequested);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Execute_ReportsIncomplete_WhenTerminationIsUncertain(int exitCode)
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(exitCode, "", TerminationUncertain: true)));

        Assert.False(outcome.Succeeded);
        Assert.Equal(exitCode, outcome.ExitCode);
        Assert.Equal("autopilot_registration_incomplete", outcome.FailureCode);
        Assert.False(outcome.HasWarnings);
    }

    [Fact]
    public async Task Execute_ReturnsTimedOut()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(null, "", TimedOut: true, TerminationUncertain: true)));

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_registration_timed_out", outcome.FailureCode);
        Assert.False(outcome.TerminationUncertain);
    }

    [Fact]
    public void ConsoleLabel_NamesTheAssistantForTechnicians() =>
        Assert.Equal("Upload hardware hash to Windows Autopilot", PostInstallProgress.GetActionName(new PreOobeExecutionAction
        { Id = "autopilot-registration", Name = "autopilot-registration", BuiltInKind = PreOobeBuiltInKind.AutopilotRegistration }));

    public void Dispose() => Directory.Delete(root, true);

    private Task<ActionStepOutcome> Execute(RecordingProcesses processes) =>
        new AutopilotRegistrationAction(root, processes, () => Now).ExecuteAsync(new PreOobeExecutionAction
        {
            Id = "autopilot-registration",
            BuiltInKind = PreOobeBuiltInKind.AutopilotRegistration,
            Parameters = JsonSerializer.SerializeToElement(new { executablePath = ExecutablePath, configPath = ConfigPath })
        }, TestContext.Current.CancellationToken);

    private sealed class RecordingProcesses(ProcessOutcome outcome) : IPreOobeProcessExecutor
    {
        public List<ProcessCommand> Commands { get; } = [];
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        { Commands.Add(command); return Task.FromResult(outcome); }
    }
}
