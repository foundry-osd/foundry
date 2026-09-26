// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Configuration;

public sealed class PreOobeConfigurationTests
{
    [Fact]
    public void GenerationKeepsOrderedEnabledActionsAndDetachedPolicyArrays()
    {
        int[] codes = [0];
        var first = Command("First") with { Process = new() { SuccessExitCodes = codes } };
        var second = PreOobeActionSettings.Create(PreOobeActionKind.Restart, "Restart");
        var settings = new PreOobeSettings { IsEnabled = true, IntegrateCustomUnattend = true, Actions = [first, Command("Disabled") with { IsEnabled = false }, second] };
        var generated = new DeployConfigurationGenerator().Generate(new() { PreOobe = settings });
        codes[0] = 9;
        Assert.Equal([first.Id, second.Id], generated.PreOobe.Actions.Select(action => action.Id));
        Assert.Equal(0, Assert.Single(generated.PreOobe.Actions[0].Process!.SuccessExitCodes));
        Assert.True(generated.PreOobe.IntegrateCustomUnattend);
    }

    [Fact]
    public void OlderProfilesDefaultToNoActionsAndPreservePortableReferences()
    {
        var old = JsonSerializer.Deserialize<FoundryConfigurationDocument>("{\"SchemaVersion\":15}")!;
        Assert.False(FoundryConfigurationMigration.ApplySchemaMigrations(old).PreOobe.IsEnabled);
        var script = PreOobeActionSettings.Create(PreOobeActionKind.PowerShell, "Script") with
        {
            Package = new() { ContentHash = new string('a', 64), DisplayName = "Script", FileCount = 1, Length = 50 },
            EntryPoint = "script.ps1"
        };
        var portable = DeploymentProfileProjection.CreatePortable(new() { PreOobe = new() { IsEnabled = true, Actions = [script] } });
        Assert.Equal(script.Package, Assert.Single(portable.PreOobe.Actions).Package);
        Assert.False(PreOobeConfigurationValidator.IsReady(portable.PreOobe, _ => false));
    }

    [Fact]
    public void RestartHasNoProcessSettingsAndRepeatedKindsRemainValid()
    {
        var restart = PreOobeActionSettings.Create(PreOobeActionKind.Restart, "Restart");
        Assert.Empty(PreOobeConfigurationValidator.Validate(new() { Actions = [Command("One"), Command("Two"), restart] }));
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [restart with { Process = new() }] }), issue => issue.Code == "PreOobe.InvalidRestartAction");
    }

    [Theory]
    [InlineData(1641, 3010)]
    [InlineData(0, 1641)]
    [InlineData(3010, 3010)]
    public void UnsafeOrOverlappingExitCodesAreRejected(int success, int restart)
    {
        var action = Command("Command") with { Process = new() { SuccessExitCodes = [success], RestartExitCodes = [restart] } };
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [action] }), issue => issue.Code == "PreOobe.InvalidProcessPolicy");
    }

    [Fact]
    public void InvalidIdsKindsAndNullListsFailWithoutExecutingAnything()
    {
        var command = Command("Command");
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [command, command] }), issue => issue.Code == "PreOobe.InvalidActionId");
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [command with { Kind = (PreOobeActionKind)999 }] }), issue => issue.Code == "PreOobe.InvalidActionKind");
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [command with { Process = new() { RestartExitCodes = null! } }] }), issue => issue.Code == "PreOobe.InvalidProcessPolicy");
    }

    [Fact]
    public void DisabledAndInapplicableMissingContentDoesNotBlockReadiness()
    {
        var application = PreOobeActionSettings.Create(PreOobeActionKind.Application, "App") with
        {
            Package = new() { ContentHash = new string('a', 64), DisplayName = "App", FileCount = 1, Length = 1 },
            EntryPoint = "setup.exe",
            Process = new() { Architecture = PreOobeArchitecture.Arm64 }
        };
        Assert.True(PreOobeConfigurationValidator.IsReady(new() { IsEnabled = true, Actions = [application with { IsEnabled = false }] }, _ => false));
        Assert.True(PreOobeConfigurationValidator.IsReady(new() { IsEnabled = true, Actions = [application] }, _ => false, PreOobeArchitecture.X64));
        Assert.False(PreOobeConfigurationValidator.IsReady(new() { IsEnabled = true, Actions = [application] }, _ => false));
    }

    private static PreOobeActionSettings Command(string name) => PreOobeActionSettings.Create(PreOobeActionKind.Command, name) with { Command = "echo ready" };

    [Theory]
    [InlineData("/forcerestart", false)]
    [InlineData("-forcerestart", false)]
    [InlineData("\"/promptrestart\"", false)]
    [InlineData("REBOOT=Force", false)]
    [InlineData("reboot=\"Suppress\"", false)]
    [InlineData("\"REBOOT=Force\"", false)]
    [InlineData("TRANSFORMS=\"unfinished", false)]
    [InlineData("TRANSFORMS=\"Company settings.mst\" REBOOT=ReallySuppress", true)]
    [InlineData("INSTALLDIR=\"C:\\Program Files\\Example\" /norestart", true)]
    public void MsiRestartArgumentsCannotOverrideControlledRestartPolicy(string arguments, bool valid)
    {
        var action = PreOobeActionSettings.Create(PreOobeActionKind.Application, "Installer") with
        {
            ApplicationMode = PreOobeApplicationMode.Msi,
            EntryPoint = "setup.msi",
            Package = new() { ContentHash = new string('a', 64), DisplayName = "Installer", FileCount = 1, Length = 1 },
            Arguments = arguments
        };
        Assert.Equal(valid, PreOobeConfigurationValidator.Validate(new() { Actions = [action] }).Count == 0);
    }
}
