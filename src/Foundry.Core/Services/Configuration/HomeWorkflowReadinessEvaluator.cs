// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Describes the presentation state of a Home workflow step.
/// </summary>
public enum HomeWorkflowState
{
    Pending,
    Ready,
    NeedsAttention
}

/// <summary>
/// Contains the evaluated state of the Home workflow steps.
/// </summary>
public sealed record HomeWorkflowReadinessEvaluation(
    HomeWorkflowState Adk,
    HomeWorkflowState General,
    HomeWorkflowState Start);

/// <summary>
/// Evaluates Home workflow steps from ADK and configuration overview readiness.
/// </summary>
public static class HomeWorkflowReadinessEvaluator
{
    /// <summary>
    /// Evaluates the ADK, General, and Start workflow steps.
    /// </summary>
    public static HomeWorkflowReadinessEvaluation Evaluate(
        bool isAdkReady,
        ConfigurationOverviewEvaluation overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        if (!isAdkReady)
        {
            return new(
                HomeWorkflowState.NeedsAttention,
                HomeWorkflowState.Pending,
                HomeWorkflowState.Pending);
        }

        bool generalNeedsAttention = ConfigurationOverviewNavigationEvaluator.EvaluateTarget(
            overview,
            ConfigurationNavigationTarget.General) == ConfigurationOverviewState.NeedsAttention;
        bool startNeedsAttention = overview.Count(ConfigurationOverviewState.NeedsAttention) > 0;

        return new(
            HomeWorkflowState.Ready,
            generalNeedsAttention ? HomeWorkflowState.NeedsAttention : HomeWorkflowState.Ready,
            startNeedsAttention ? HomeWorkflowState.NeedsAttention : HomeWorkflowState.Ready);
    }
}
