using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentRuntimeContextServiceTests : IDisposable
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";
    private readonly string? _originalMode = Environment.GetEnvironmentVariable(DeploymentModeEnvironmentVariable);

    [Fact]
    public void Resolve_WhenEnvironmentModeIsRecovery_ReturnsRecoveryContext()
    {
        Environment.SetEnvironmentVariable(DeploymentModeEnvironmentVariable, "Recovery");
        var service = new DeploymentRuntimeContextService();

        DeploymentRuntimeContext context = service.Resolve();

        Assert.Equal(DeploymentMode.Recovery, context.Mode);
        Assert.Null(context.UsbCacheRuntimeRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DeploymentModeEnvironmentVariable, _originalMode);
    }
}
