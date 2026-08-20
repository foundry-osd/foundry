// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class HomeWorkflowReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_AdkNotReady_BlocksAdkAndLeavesFollowingStepsPending()
    {
        ConfigurationOverviewEvaluation overview = EvaluateOverview(new FoundryConfigurationDocument());

        HomeWorkflowReadinessEvaluation evaluation = HomeWorkflowReadinessEvaluator.Evaluate(false, overview);

        Assert.Equal(HomeWorkflowState.NeedsAttention, evaluation.Adk);
        Assert.Equal(HomeWorkflowState.Pending, evaluation.General);
        Assert.Equal(HomeWorkflowState.Pending, evaluation.Start);
    }

    [Fact]
    public void Evaluate_GeneralNeedsAttention_BlocksGeneralAndStart()
    {
        ConfigurationOverviewEvaluation overview = ConfigurationOverviewEvaluator.Evaluate(
            CreateContext(new FoundryConfigurationDocument
            {
                General = new GeneralSettings
                {
                    DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true }
                }
            }) with
            { IsDeploymentProtectionSecretReady = false });

        HomeWorkflowReadinessEvaluation evaluation = HomeWorkflowReadinessEvaluator.Evaluate(true, overview);

        Assert.Equal(HomeWorkflowState.Ready, evaluation.Adk);
        Assert.Equal(HomeWorkflowState.NeedsAttention, evaluation.General);
        Assert.Equal(HomeWorkflowState.NeedsAttention, evaluation.Start);
    }

    [Fact]
    public void Evaluate_NonGeneralItemNeedsAttention_BlocksOnlyStart()
    {
        ConfigurationOverviewEvaluation overview = EvaluateOverview(new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { IsEnabled = true }
            }
        });

        HomeWorkflowReadinessEvaluation evaluation = HomeWorkflowReadinessEvaluator.Evaluate(true, overview);

        Assert.Equal(HomeWorkflowState.Ready, evaluation.Adk);
        Assert.Equal(HomeWorkflowState.Ready, evaluation.General);
        Assert.Equal(HomeWorkflowState.NeedsAttention, evaluation.Start);
    }

    [Fact]
    public void Evaluate_AllItemsReady_CompletesEveryStep()
    {
        ConfigurationOverviewEvaluation overview = EvaluateOverview(new FoundryConfigurationDocument());

        HomeWorkflowReadinessEvaluation evaluation = HomeWorkflowReadinessEvaluator.Evaluate(true, overview);

        Assert.Equal(HomeWorkflowState.Ready, evaluation.Adk);
        Assert.Equal(HomeWorkflowState.Ready, evaluation.General);
        Assert.Equal(HomeWorkflowState.Ready, evaluation.Start);
    }

    private static ConfigurationOverviewEvaluation EvaluateOverview(FoundryConfigurationDocument configuration) =>
        ConfigurationOverviewEvaluator.Evaluate(CreateContext(configuration));

    private static ConfigurationOverviewContext CreateContext(FoundryConfigurationDocument configuration) => new()
    {
        Configuration = configuration,
        EffectiveNetwork = configuration.Network,
        IsWinPeLanguageReady = true,
        IsCustomDriverConfigurationReady = true,
        IsDeploymentProtectionSecretReady = true,
        IsAutopilotConfigurationReady = true
    };
}
