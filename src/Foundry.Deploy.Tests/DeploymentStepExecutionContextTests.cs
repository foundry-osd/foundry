using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStepExecutionContextTests
{
    [Fact]
    public void ResolvePreferredHash_PrefersPrimaryHashWhenPresent()
    {
        string hash = DeploymentStepExecutionContext.ResolvePreferredHash("  ABC123  ", "DEF456");

        Assert.Equal("ABC123", hash);
    }

    [Fact]
    public void ResolvePreferredHash_FallsBackToSecondaryHash()
    {
        string hash = DeploymentStepExecutionContext.ResolvePreferredHash(null, "  DEF456  ");

        Assert.Equal("DEF456", hash);
    }

    [Fact]
    public void ResolveFileName_WhenPreferredFileNameExists_SanitizesPreferredName()
    {
        string fileName = DeploymentStepExecutionContext.ResolveFileName("  setup<>.wim  ", "https://example.test/ignored.iso");

        Assert.Equal("setup__.wim", fileName);
    }

    [Fact]
    public void ResolveFileName_WhenPreferredFileNameMissing_UsesSourceUrlFileName()
    {
        string fileName = DeploymentStepExecutionContext.ResolveFileName("", "https://example.test/files/install.esd");

        Assert.Equal("install.esd", fileName);
    }

    [Fact]
    public void SanitizePathSegment_WhenValueIsBlank_ReturnsFallbackItem()
    {
        string sanitized = DeploymentStepExecutionContext.SanitizePathSegment("   ");

        Assert.Equal("item", sanitized);
    }
}
