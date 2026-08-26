// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Tests;

public sealed class ApplyRecoveryDriversStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOfflineInfAndRecoveryConfigured_AppliesDriversOnlyToRecovery()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        var step = new ApplyRecoveryDriversStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
        Assert.Equal(1, fixture.DeploymentService.RecoveryApplyCount);
    }

    [Theory]
    [InlineData(DriverPackInstallMode.None)]
    [InlineData(DriverPackInstallMode.DeferredSetupComplete)]
    public async Task ExecuteAsync_WhenRecoveryServicingIsNotRequired_Skips(DriverPackInstallMode installMode)
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.DriverPackInstallMode = installMode;
        var step = new ApplyRecoveryDriversStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryIsNotConfigured_Fails()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.WinReConfigured = false;
        var step = new ApplyRecoveryDriversStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryPartitionIsUnavailable_Fails()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.TargetRecoveryPartitionRoot = null;
        var step = new ApplyRecoveryDriversStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDryRunAndRecoveryConfigured_SimulatesWithoutServicingRecovery()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext(isDryRun: true);
        var step = new ApplyRecoveryDriversStep(fixture.DeploymentService);

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(0, fixture.DeploymentService.WindowsApplyCount);
        Assert.Equal(0, fixture.DeploymentService.RecoveryApplyCount);
    }
}
