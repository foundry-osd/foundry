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
    private const string LegacyScriptPath = "Runtime/AutopilotRegistration/Start-FoundryAutopilotRegistration.ps1";
    private const string LegacyConfigPath = "Runtime/AutopilotRegistration/config.json";
    private const string WindowsRoot = @"C:\Windows";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 30, 15, 250, TimeSpan.Zero);
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));

    public AutopilotRegistrationActionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Runtime", "AutopilotInteractive"));
        File.WriteAllText(Path.Combine(root, ExecutablePath), "fixture");
        File.WriteAllText(Path.Combine(root, ConfigPath), "{}");
        Directory.CreateDirectory(Path.Combine(root, "Runtime", "AutopilotRegistration"));
        File.WriteAllText(Path.Combine(root, LegacyScriptPath), "fixture");
        File.WriteAllText(Path.Combine(root, LegacyConfigPath), "{}");
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

    [Fact]
    public async Task LegacyScriptPath_RunsTheOriginalPowerShellCommand()
    {
        var processes = new RecordingProcesses(new(0, ""));

        await Execute(processes, new { scriptPath = LegacyScriptPath, configPath = LegacyConfigPath });

        ProcessCommand command = Assert.Single(processes.Commands);
        string script = Path.Combine(root, "Runtime", "AutopilotRegistration", "Start-FoundryAutopilotRegistration.ps1");
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", command.FileName);
        Assert.Equal(
            ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-STA", "-WindowStyle", "Hidden", "-File", script,
             "-ConfigPath", Path.Combine(root, "Runtime", "AutopilotRegistration", "config.json")],
            command.Arguments);
        Assert.Equal(Path.GetDirectoryName(script), command.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(60), command.Timeout);
        Assert.Equal(Path.Combine(root, "Logs", "PreOobe", "autopilot-registration", "Process", "output.log"), command.OutputPath);
    }

    [Fact]
    public async Task LegacyScript_ExitZero_Succeeds()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(0, "")),
            new { scriptPath = LegacyScriptPath, configPath = LegacyConfigPath });

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Null(outcome.FailureCode);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public async Task LegacyScript_NonZero_FailsIncomplete(int exitCode, bool uncertain)
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(exitCode, "", TerminationUncertain: uncertain)),
            new { scriptPath = LegacyScriptPath, configPath = LegacyConfigPath });

        Assert.False(outcome.Succeeded);
        Assert.Equal(exitCode, outcome.ExitCode);
        Assert.Equal("autopilot_registration_incomplete", outcome.FailureCode);
        Assert.False(outcome.HasWarnings);
    }

    [Fact]
    public async Task LegacyScript_TimedOut_FailsTimedOut()
    {
        ActionStepOutcome outcome = await Execute(new RecordingProcesses(new(null, "", TimedOut: true, TerminationUncertain: true)),
            new { scriptPath = LegacyScriptPath, configPath = LegacyConfigPath });

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_registration_timed_out", outcome.FailureCode);
    }

    [Fact]
    public async Task LegacyScript_ReturnsAssistantMissing_WhenScriptAbsent()
    {
        File.Delete(Path.Combine(root, LegacyScriptPath));
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes, new { scriptPath = LegacyScriptPath, configPath = LegacyConfigPath });

        Assert.Equal("autopilot_assistant_missing", outcome.FailureCode);
        Assert.Empty(processes.Commands);
    }

    [Fact]
    public async Task LegacyFixture_FromV26_10_18_1Deploy_RunsTheLegacyAssistant()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "autopilot-registration-v26.10.18.1.json"));
        using JsonDocument parameters = JsonDocument.Parse(json);
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes, parameters.RootElement.Clone());

        Assert.True(outcome.Succeeded);
        ProcessCommand command = Assert.Single(processes.Commands);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", command.FileName);
        Assert.Equal(Path.Combine(root, "Runtime", "AutopilotRegistration", "Start-FoundryAutopilotRegistration.ps1"), command.Arguments[8]);
        Assert.Equal(Path.Combine(root, "Runtime", "AutopilotRegistration", "config.json"), command.Arguments[10]);
    }

    [Fact]
    public async Task BothPaths_ExecutablePathWins()
    {
        var processes = new RecordingProcesses(new(0, ""));

        await Execute(processes, new { executablePath = ExecutablePath, scriptPath = LegacyScriptPath, configPath = ConfigPath });

        ProcessCommand command = Assert.Single(processes.Commands);
        Assert.Equal(Path.Combine(root, "Runtime", "AutopilotInteractive", "Foundry.AutopilotInteractive.exe"), command.FileName);
        Assert.Equal(AutopilotRegistrationAction.Timeout, command.Timeout);
    }

    [Fact]
    public async Task NoPath_FailsAssistantMissing()
    {
        var processes = new RecordingProcesses(new(0, ""));

        ActionStepOutcome outcome = await Execute(processes, new { configPath = ConfigPath });

        Assert.False(outcome.Succeeded);
        Assert.Equal("autopilot_assistant_missing", outcome.FailureCode);
        Assert.Empty(processes.Commands);
    }

    public void Dispose() => Directory.Delete(root, true);

    private Task<ActionStepOutcome> Execute(RecordingProcesses processes) =>
        Execute(processes, new { executablePath = ExecutablePath, configPath = ConfigPath });

    private Task<ActionStepOutcome> Execute(RecordingProcesses processes, object parameters) =>
        Execute(processes, JsonSerializer.SerializeToElement(parameters));

    private Task<ActionStepOutcome> Execute(RecordingProcesses processes, JsonElement parameters) =>
        new AutopilotRegistrationAction(root, WindowsRoot, processes, () => Now).ExecuteAsync(new PreOobeExecutionAction
        {
            Id = "autopilot-registration",
            BuiltInKind = PreOobeBuiltInKind.AutopilotRegistration,
            Parameters = parameters
        }, TestContext.Current.CancellationToken);

    private sealed class RecordingProcesses(ProcessOutcome outcome) : IPreOobeProcessExecutor
    {
        public List<ProcessCommand> Commands { get; } = [];
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        { Commands.Add(command); return Task.FromResult(outcome); }
    }
}
