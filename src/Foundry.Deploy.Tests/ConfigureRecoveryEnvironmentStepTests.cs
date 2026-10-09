// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;

namespace Foundry.Deploy.Tests;

public sealed class ConfigureRecoveryEnvironmentStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenAppliedImageHasWinRe_ConfiguresRecovery()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.WinReConfigured = false;

        DeploymentStepResult result = await new ConfigureRecoveryEnvironmentStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(1, fixture.DeploymentService.RecoveryConfigureCount);
        Assert.True(context.RuntimeState.WinReConfigured);
        Assert.False(context.RuntimeState.AppliedImageHasNoWinRe);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAppliedImageHasNoWinRe_SkipsAndRecordsTheAbsence()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.WinReConfigured = false;
        fixture.DeploymentService.AppliedImageHasWinRe = false;

        DeploymentStepResult result = await new ConfigureRecoveryEnvironmentStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Skipped, result.State);
        Assert.Equal("The applied Windows image does not contain winre.wim.", result.Message);
        Assert.False(context.RuntimeState.WinReConfigured);
        Assert.True(context.RuntimeState.AppliedImageHasNoWinRe);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConfigurationFails_StillFails()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.WinReConfigured = false;
        fixture.DeploymentService.RecoveryConfigureFailure = new FileNotFoundException("winrecfg.exe was not found.");

        await Assert.ThrowsAsync<FileNotFoundException>(() => new ConfigureRecoveryEnvironmentStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken));

        Assert.False(context.RuntimeState.WinReConfigured);
        Assert.False(context.RuntimeState.AppliedImageHasNoWinRe);
    }

    [Fact]
    public async Task SealStep_WhenAppliedImageHasNoWinRe_StillHidesRecoveryPartition()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        DeploymentStepExecutionContext context = fixture.CreateContext();
        context.RuntimeState.AppliedImageHasNoWinRe = true;

        DeploymentStepResult result = await new SealRecoveryPartitionStep(fixture.DeploymentService)
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(1, fixture.DeploymentService.RecoverySealCount);
    }
}
