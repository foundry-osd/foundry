using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptDefinitionBuilderTests
{
    [Fact]
    public void Build_WhenAppxRemovalIsEnabled_PassesPackageNamesAsSingleArgument()
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

        Assert.Equal("remove-appx", script.Id);
        Assert.Equal(["-PackageNames", "Microsoft.BingWeather,Microsoft.Copilot"], script.Arguments);
    }
}
