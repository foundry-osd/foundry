// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Describes the presentation state of a Home workflow step.
/// </summary>
public enum HomeWorkflowState
{
    Pending,
    Ready,
    NeedsAttention,

    /// <summary>The step waits for a follower to finish syncing its shared content; it becomes ready by itself.</summary>
    Syncing
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
    /// Evaluates the ADK, General, and Start workflow steps. Media creation is blocked until a follower has all its shared content,
    /// so Start reads <see cref="HomeWorkflowState.Syncing"/> while it arrives and needs attention when it failed or stalled.
    /// </summary>
    public static HomeWorkflowReadinessEvaluation Evaluate(
        bool isAdkReady,
        ConfigurationOverviewEvaluation overview,
        SharedContentSyncState sharedContent = SharedContentSyncState.Synced)
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
        bool startNeedsAttention = overview.NeedsAttentionCount > 0;

        return new(
            HomeWorkflowState.Ready,
            generalNeedsAttention ? HomeWorkflowState.NeedsAttention : HomeWorkflowState.Ready,
            startNeedsAttention || sharedContent is SharedContentSyncState.Failed or SharedContentSyncState.Stalled
                ? HomeWorkflowState.NeedsAttention
                : sharedContent == SharedContentSyncState.Syncing ? HomeWorkflowState.Syncing : HomeWorkflowState.Ready);
    }
}
