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
                    "Microsoft.Copilot"
                ]
            });

        PreOobeScriptDefinition script = Assert.Single(scripts);
        PreOobeScriptDataFile dataFile = Assert.Single(script.DataFiles);

        Assert.Equal("remove-appx", script.Id);
        Assert.Empty(script.Arguments);
        Assert.Equal("Remove-AppX.packages.json", dataFile.FileName);
        Assert.Contains("\"packageName\": \"Microsoft.BingWeather\"", dataFile.Content);
        Assert.Contains("\"packageName\": \"Microsoft.Copilot\"", dataFile.Content);
    }
}
