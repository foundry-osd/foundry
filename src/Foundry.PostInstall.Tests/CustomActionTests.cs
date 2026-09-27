// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class CustomActionTests
{
    [Theory]
    [InlineData(PreOobeActionKind.PowerShell, null, "script.ps1", "powershell.exe")]
    [InlineData(PreOobeActionKind.Application, PreOobeApplicationMode.Msi, "app.msi", "msiexec.exe")]
    [InlineData(PreOobeActionKind.Application, PreOobeApplicationMode.Exe, "app.exe", "app.exe")]
    public async Task PackagedAction_IgnoresLegacyArchitectureAndUsesExpectedHostAndRestartClassification(PreOobeActionKind kind, PreOobeApplicationMode? mode,
        string entryPoint, string host)
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "Payloads", "package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, entryPoint), "fixture");
        try
        {
            var plan = new PreOobeExecutionPlan
            { OperationId = "operation", Packages = [new() { ContentHash = "hash", RelativePath = "Payloads/package", Manifest = new() }] };
            var process = new Recorder(new(3010, ""));
            var action = new PreOobeExecutionAction
            {
                Id = "action",
                CustomAction = new()
                {
                    Kind = kind,
                    ApplicationMode = mode,
                    EntryPoint = entryPoint,
                    Package = new() { ContentHash = "hash" },
                    Arguments = "PROPERTY=value",
                    Process = JsonSerializer.Deserialize<PreOobeProcessSettings>("""{"Architecture":999,"RestartExitCodes":[3010]}""")
                }
            };
            ActionStepOutcome result = await new CustomAction(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows), plan, process)
                .ExecuteAsync(action, TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded);
            Assert.True(result.RestartRequested);
            Assert.EndsWith(host, process.Command!.FileName);
            Assert.Equal(package, process.Command.WorkingDirectory);
            if (mode == PreOobeApplicationMode.Msi) Assert.EndsWith("REBOOT=ReallySuppress /qn /norestart", process.Command.RawArguments);
            if (kind == PreOobeActionKind.PowerShell) Assert.Contains("-NonInteractive", process.Command.Arguments);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InstallerInitiatedRestart_CannotBeAcceptedAsSuccess()
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var action = new PreOobeExecutionAction { Id = "command", CustomAction = new() { Kind = PreOobeActionKind.Command, Command = "echo value", Process = new() } };
            ActionStepOutcome result = await new CustomAction(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                new() { OperationId = "operation" }, new Recorder(new(1641, ""))).ExecuteAsync(action, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.True(result.TerminationUncertain);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Localization_ResolvesNeutralResources() =>
        Assert.Equal("Post-installation completed.", LocalizationText.Create("en-US").GetString("PostInstall.Succeeded"));

    private sealed class Recorder(ProcessOutcome result) : IPreOobeProcessExecutor
    {
        public ProcessCommand? Command { get; private set; }
        public Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
        { Command = command; return Task.FromResult(result); }
    }
}
