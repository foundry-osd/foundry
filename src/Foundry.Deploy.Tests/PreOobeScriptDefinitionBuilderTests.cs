using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptDefinitionBuilderTests
{
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
    public void Build_WhenAiComponentRemovalIsEnabled_StagesSettingsDataFile()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings
            {
                IsEnabled = true,
                RemoveCopilot = true,
                RemoveAiHub = true,
                DisableRecall = true,
                DisableClickToDo = true,
                DisableAiServiceAutoStart = true,
                DisableEdgeAi = true,
                DisablePaintAi = true,
                DisableNotepadAi = true
            });

        PreOobeScriptDefinition script = Assert.Single(scripts);
        PreOobeScriptDataFile dataFile = Assert.Single(script.DataFiles);

        Assert.Equal("remove-ai-components", script.Id);
        Assert.Equal("Remove-AiComponents.ps1", script.FileName);
        Assert.Equal(PreOobeScriptResources.RemoveAiComponents, script.ResourceName);
        Assert.Empty(script.Arguments);
        Assert.Equal("Remove-AiComponents.settings.json", dataFile.FileName);
        Assert.Contains("\"removeCopilot\": true", dataFile.Content);
        Assert.Contains("\"removeAiHub\": true", dataFile.Content);
        Assert.Contains("\"disableRecall\": true", dataFile.Content);
        Assert.Contains("\"disableClickToDo\": true", dataFile.Content);
        Assert.Contains("\"disableAiServiceAutoStart\": true", dataFile.Content);
        Assert.Contains("\"disableEdgeAi\": true", dataFile.Content);
        Assert.Contains("\"disablePaintAi\": true", dataFile.Content);
        Assert.Contains("\"disableNotepadAi\": true", dataFile.Content);
    }

    [Fact]
    public void Build_WhenAiComponentRemovalHasNoSelectedOptions_DoesNotStageAiScript()
    {
        var builder = new PreOobeScriptDefinitionBuilder();

        IReadOnlyList<PreOobeScriptDefinition> scripts = builder.Build(
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings
            {
                IsEnabled = true
            });

        Assert.Empty(scripts);
    }
}
