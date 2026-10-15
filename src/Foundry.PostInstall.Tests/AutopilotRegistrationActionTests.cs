// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class AutopilotRegistrationActionTests : IDisposable
{
    private const string ScriptPath = "Runtime/AutopilotRegistration/Start-FoundryAutopilotRegistration.ps1";
    private const string ConfigPath = "Runtime/AutopilotRegistration/config.json";
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
    private readonly string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public AutopilotRegistrationActionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Runtime", "AutopilotRegistration"));
        File.WriteAllText(Path.Combine(root, ScriptPath), "fixture");
        File.WriteAllText(Path.Combine(root, ConfigPath), "{}");
    }

    [Fact]
    public async Task CompletedAssistant_RunsStagedScriptInteractively()
    {
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes);

        Assert.True(outcome.Succeeded);
        ProcessCommand command = Assert.Single(processes.Commands);
        Assert.Equal(Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"), command.FileName);
        Assert.Contains("-STA", command.Arguments);
        Assert.Equal(Path.Combine(root, "Runtime", "AutopilotRegistration", "Start-FoundryAutopilotRegistration.ps1"),
            command.Arguments[command.Arguments.ToList().IndexOf("-File") + 1]);
        Assert.Equal(Path.Combine(root, "Runtime", "AutopilotRegistration", "config.json"),
            command.Arguments[command.Arguments.ToList().IndexOf("-ConfigPath") + 1]);
        Assert.Equal(AutopilotRegistrationAction.Timeout, command.Timeout);
    }

    [Fact]
    public async Task MissingStagedScript_FailsWithoutStartingProcess()
    {
        File.Delete(Path.Combine(root, ScriptPath));
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes);

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_assistant_missing", outcome.FailureCode);
        Assert.Empty(processes.Commands);
    }

    [Fact]
    public async Task ClosedAssistant_ReportsIncompleteRegistration()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(1, "")));

        Assert.False(outcome.Succeeded);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal("autopilot_registration_incomplete", outcome.FailureCode);
        Assert.False(outcome.TerminationUncertain);
    }

    [Fact]
    public async Task TimedOutAssistant_IsAnOrdinaryFailure()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(null, "", TimedOut: true, TerminationUncertain: true)));

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_registration_timed_out", outcome.FailureCode);
        Assert.False(outcome.TerminationUncertain);
    }

    public void Dispose() => Directory.Delete(root, true);

    private Task<ActionStepOutcome> Execute(RecordingProcesses processes) =>
        new AutopilotRegistrationAction(root, windows, processes).ExecuteAsync(new PreOobeExecutionAction
        {
            Id = "autopilot-registration",
            BuiltInKind = PreOobeBuiltInKind.AutopilotRegistration,
            Parameters = JsonSerializer.SerializeToElement(new { scriptPath = ScriptPath, configPath = ConfigPath })
        }, TestContext.Current.CancellationToken);

    private sealed class RecordingProcesses(ProcessOutcome outcome) : IPreOobeProcessExecutor
    {
        public List<ProcessCommand> Commands { get; } = [];
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        { Commands.Add(command); return Task.FromResult(outcome); }
    }
}
