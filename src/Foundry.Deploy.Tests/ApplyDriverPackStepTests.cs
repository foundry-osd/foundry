// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Tests;

public sealed class ApplyDriverPackStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOfflineInfAndRecoveryConfigured_AppliesDriversOnlyToWindows()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        var step = new ApplyDriverPackStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(1, fixture.DeploymentService.WindowsApplyCount);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverPackIsDeferred_SkipsWithoutStaging()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        string packagePath = fixture.CreateDriverPackage();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = packagePath;
        var step = new ApplyDriverPackStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal("Driver pack prepared for deferred installation.", result.Message);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }
}
