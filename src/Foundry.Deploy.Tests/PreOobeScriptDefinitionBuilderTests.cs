// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptDefinitionBuilderTests
{
    [Theory]
    [InlineData("none", false)]
    [InlineData("blank-appx", false)]
    [InlineData("disabled-appx", false)]
    [InlineData("appx", true)]
    [InlineData("ai-policy", false)]
    [InlineData("ai-hub", true)]
    [InlineData("driver", true)]
    [InlineData("network", true)]
    [InlineData("activation", true)]
    public void HasScripts_OnlyIncludesEffectiveSetupTasks(string selection, bool expected)
    {
        var appx = new DeployAppxRemovalSettings
        {
            IsEnabled = selection is "blank-appx" or "appx",
            PackageNames = selection switch
            {
                "blank-appx" => [" "],
                "appx" or "disabled-appx" => ["Microsoft.BingNews"],
                _ => []
            }
        };
        var ai = new DeployAiComponentRemovalSettings
        {
            IsEnabled = true,
            DisableRecall = selection == "ai-policy",
            RemoveAiHub = selection == "ai-hub"
        };

        bool result = PreOobeScriptDefinitionBuilder.HasScripts(appx, ai,
            selection == "driver", selection == "network", selection == "activation");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Build_WhenAppxRemovalIsEnabled_StagesPackageCatalogDataFile()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames =
                [
                    "Microsoft.BingWeather",
                    "Microsoft.BingNews"
                ]
            });

        PreOobeScriptDefinition script = Assert.Single(scripts);
        PreOobeScriptDataFile dataFile = Assert.Single(script.DataFiles);

        Assert.Equal("remove-appx", script.Id);
        Assert.Empty(script.Arguments);
        Assert.Equal("Remove-AppX.packages.json", dataFile.FileName);
        Assert.Contains("\"packageName\": \"Microsoft.BingWeather\"", dataFile.Content);
        Assert.Contains("\"packageName\": \"Microsoft.BingNews\"", dataFile.Content);
    }

    [Fact]
    public void Build_WhenAiComponentRemovalHasAppxOptions_StagesAppxPackageDataFile()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings
            {
                IsEnabled = true,
                RemoveCopilot = true,
                RemoveAiHub = true
            });

        PreOobeScriptDefinition script = Assert.Single(scripts);
        PreOobeScriptDataFile dataFile = Assert.Single(script.DataFiles);

        Assert.Equal("remove-ai-components", script.Id);
        Assert.Equal("Remove-AiComponents.ps1", script.FileName);
        Assert.Equal(PreOobeScriptResources.RemoveAiComponents, script.ResourceName);
        Assert.Empty(script.Arguments);
        Assert.Equal("Remove-AiComponents.settings.json", dataFile.FileName);
        Assert.Contains("\"appxPackages\":", dataFile.Content);
        Assert.Contains("\"packageName\": \"Microsoft.Copilot\"", dataFile.Content);
        Assert.Contains("\"packageName\": \"Microsoft.Windows.AIHub\"", dataFile.Content);
        Assert.DoesNotContain("disableRecall", dataFile.Content);
        Assert.DoesNotContain("disableEdgeAi", dataFile.Content);
    }

    [Fact]
    public void Build_WhenAiComponentRemovalHasOnlyRegistryOptions_DoesNotStageAiScript()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings
            {
                IsEnabled = true,
                DisableRecall = true,
                DisableEdgeAi = true,
                DisableNotepadAi = true
            });

        Assert.Empty(scripts);
    }

    [Fact]
    public void Build_WhenNetworkProfileRoamingPayloadExists_StagesImportAndCleanupScripts()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings(),
            networkProfileRoaming: new PreOobeNetworkProfileRoamingPayload
            {
                DataFiles =
                [
                    new PreOobeScriptDataFile
                    {
                        FileName = @"NetworkProfiles\import-settings.json",
                        Content = "{}"
                    }
                ]
            });

        Assert.Collection(
            scripts,
            script =>
            {
                Assert.Equal("network-profile-roaming", script.Id);
                Assert.Equal("Import-NetworkProfiles.ps1", script.FileName);
                Assert.Equal(PreOobeScriptPriority.NetworkProfileImport, script.Priority);
            },
            script =>
            {
                Assert.Equal("cleanup", script.Id);
                Assert.Equal(PreOobeScriptPriority.Cleanup, script.Priority);
            });
    }
}
