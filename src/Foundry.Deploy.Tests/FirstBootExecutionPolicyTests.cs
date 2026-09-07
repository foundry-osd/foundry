// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class FirstBootExecutionPolicyTests
{
    [Theory]
    [InlineData("Professional", "OEM:DM", true)]
    [InlineData("Professional", "Retail", false)]
    [InlineData("Professional", "", false)]
    [InlineData("Unknown", "", false)]
    public void RequiredUnqualifiedHooks_AreRejectedBeforeDeployment(string edition, string channel, bool known)
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate(edition, channel, false, false, known, true, true);
        Assert.Equal("unsupported_setup_hook", plan.FailureCode);
        Assert.False(plan.CanLaunchInteractiveRegistration);
    }

    [Theory]
    [InlineData("Enterprise", "OEM:DM", false)]
    [InlineData("EnterpriseS", "", false)]
    [InlineData("ServerStandard", "", false)]
    [InlineData("Professional", "Retail", true)]
    [InlineData("Professional", "Volume", true)]
    public void ProvenSetupCompleteScenario_UsesItsHook(string edition, string channel, bool known)
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate(edition, channel, false, false, known, true, true);
        Assert.Null(plan.FailureCode);
        Assert.Equal(FirstBootEntryPoint.SetupComplete, plan.CustomizationEntryPoint);
        Assert.True(plan.CanLaunchInteractiveRegistration);
    }

    [Fact]
    public void NoHookDependentFeatures_DoesNotRequireLicenseEvidence()
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate("Professional", "", true, false, false, false, false);
        Assert.Null(plan.FailureCode);
        Assert.Equal(FirstBootEntryPoint.None, plan.CustomizationEntryPoint);
    }

    [Theory]
    [InlineData(false, false, FirstBootEntryPoint.GeneratedSpecialize)]
    [InlineData(true, true, FirstBootEntryPoint.VerifiedCustomSpecialize)]
    public void QualifiedNoninteractiveActions_CanUseSpecialize(bool custom, bool recognized, FirstBootEntryPoint expected)
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate("Professional", "OEM:DM", custom, recognized,
            true, true, false, areSpecializeActionsQualified: true);
        Assert.Null(plan.FailureCode);
        Assert.Equal(expected, plan.CustomizationEntryPoint);
        Assert.False(plan.CanLaunchInteractiveRegistration);
    }

    [Fact]
    public void CustomUnknownCommand_IsNotProofOfTheSupportedLauncher()
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate("Professional", "OEM:DM", true, false,
            true, true, false, areSpecializeActionsQualified: true);
        Assert.Equal("unsupported_setup_hook", plan.FailureCode);
    }

    [Fact]
    public void QualifiedSpecialize_DoesNotAuthorizeAnInteractiveOobeLaunch()
    {
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate("Professional", "OEM:DM", false, false,
            true, true, true, areSpecializeActionsQualified: true);
        Assert.Equal("unsupported_setup_hook", plan.FailureCode);
        Assert.False(plan.CanLaunchInteractiveRegistration);
    }
}
