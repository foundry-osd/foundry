// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Configuration;

public sealed class PreOobeConfigurationTests
{
    [Fact]
    public void AuthoringAndDeploymentRoundTripsPreserveArgumentAndLoggingOptions()
    {
        var package = new PreOobePackageReference { ContentHash = new string('a', 64), DisplayName = "Content", FileCount = 1, Length = 1 };
        var script = PreOobeActionSettings.Create(PreOobeActionKind.PowerShell, "Script") with
        { Package = package, EntryPoint = "script.ps1", PowerShellArguments = "-NoProfile", Arguments = "-Name Example" };
        var installer = PreOobeActionSettings.Create(PreOobeActionKind.Application, "Software") with
        { Package = package, EntryPoint = "app.msi", ApplicationMode = PreOobeApplicationMode.Msi, Arguments = "/quiet", GenerateInstallationLog = true };
        var service = new FoundryConfigurationService();
        var source = service.Deserialize(service.Serialize(new() { PreOobe = new() { IsEnabled = true, Actions = [script, installer] } }));
        var generator = new DeployConfigurationGenerator();
        var generated = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(generator.Serialize(generator.Generate(source)), ConfigurationJsonDefaults.SerializerOptions)!;
        Assert.Equal("-NoProfile", generated.PreOobe.Actions[0].PowerShellArguments);
        Assert.Equal("-Name Example", generated.PreOobe.Actions[0].Arguments);
        Assert.True(generated.PreOobe.Actions[1].GenerateInstallationLog);
        Assert.Equal("/quiet", generated.PreOobe.Actions[1].Arguments);
    }

    [Theory]
    [InlineData("-NoProfile\n-Command test")]
    [InlineData("-NoProfile\0")]
    public void PowerShellHostArgumentsRejectUnsupportedControlCharacters(string arguments)
    {
        var action = PreOobeActionSettings.Create(PreOobeActionKind.PowerShell, "Script") with
        {
            Package = new() { ContentHash = new string('a', 64), DisplayName = "Content", FileCount = 1, Length = 1 },
            EntryPoint = "script.ps1",
            PowerShellArguments = arguments
        };
        Assert.Contains(PreOobeConfigurationValidator.Validate(new() { Actions = [action] }), issue => issue.Code == "PreOobe.InvalidCommand");
    }

    [Fact]
    public void GenerationKeepsOrderedEnabledActionsAndDetachedPolicyArrays()
    {
        int[] codes = [0];
        var first = Command("First") with { Process = new() { SuccessExitCodes = codes } };
        var second = PreOobeActionSettings.Create(PreOobeActionKind.Restart, "Restart");
        var settings = new PreOobeSettings { IsEnabled = true, Actions = [first, Command("Disabled") with { IsEnabled = false }, second] };
        var generated = new DeployConfigurationGenerator().Generate(new() { PreOobe = settings });
        codes[0] = 9;
        Assert.Equal([first.Id, second.Id], generated.PreOobe.Actions.Select(action => action.Id));
        Assert.Equal(0, Assert.Single(generated.PreOobe.Actions[0].Process!.SuccessExitCodes));
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
    public void MissingContentBlocksEveryEnabledActionButNotDisabledActions()
    {
        var application = PreOobeActionSettings.Create(PreOobeActionKind.Application, "App") with
        {
            Package = new() { ContentHash = new string('a', 64), DisplayName = "App", FileCount = 1, Length = 1 },
            EntryPoint = "setup.exe"
        };
        Assert.True(PreOobeConfigurationValidator.IsReady(new() { IsEnabled = true, Actions = [Command("Enabled"), application with { IsEnabled = false }] }, _ => false));
        Assert.False(PreOobeConfigurationValidator.IsReady(new() { IsEnabled = true, Actions = [application] }, _ => false));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void EmptyOrFullyDisabledDraftsAreEditableButNotReadyWhenEnabled(bool enabled, bool hasDisabledAction, bool ready)
    {
        var settings = new PreOobeSettings
        {
            IsEnabled = enabled,
            Actions = hasDisabledAction ? [Command("Disabled") with { IsEnabled = false }] : []
        };

        Assert.Empty(PreOobeConfigurationValidator.Validate(settings));
        Assert.Equal(ready, PreOobeConfigurationValidator.IsReady(settings, _ => true));
    }

    private static PreOobeActionSettings Command(string name) => PreOobeActionSettings.Create(PreOobeActionKind.Command, name) with { Command = "echo ready" };

    [Theory]
    [InlineData("/forcerestart", true)]
    [InlineData("-forcerestart", true)]
    [InlineData("\"/promptrestart\"", true)]
    [InlineData("REBOOT=Force", true)]
    [InlineData("reboot=\"Suppress\"", true)]
    [InlineData("\"REBOOT=Force\"", true)]
    [InlineData("TRANSFORMS=\"unfinished", true)]
    [InlineData("TRANSFORMS=\"Company settings.mst\" REBOOT=ReallySuppress", true)]
    [InlineData("INSTALLDIR=\"C:\\Program Files\\Example\" /norestart", true)]
    public void MsiArgumentsRemainAdministratorOwned(string arguments, bool valid)
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
