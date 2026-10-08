// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Retains inspected edition evidence and domain-only warnings without changing the frozen request.</summary>
internal static class DomainJoinRuntimeEligibility
{
    internal static void Initialize(DeploymentContext request, DeploymentRuntimeState state)
    {
        state.DomainJoinStatus = request.DomainJoinRequest?.Disposition switch
        {
            Services.DomainJoin.DomainJoinDeploymentDisposition.UnsupportedEdition => DomainJoinExecutionStatus.SkippedUnsupportedEdition,
            Services.DomainJoin.DomainJoinDeploymentDisposition.Ready or Services.DomainJoin.DomainJoinDeploymentDisposition.DryRun => DomainJoinExecutionStatus.Pending,
            _ => DomainJoinExecutionStatus.Disabled
        };
        if (state.DomainJoinStatus == DomainJoinExecutionStatus.SkippedUnsupportedEdition)
            state.DomainJoinSkipCode = DomainJoinSkipCode.UnsupportedEdition;
    }

    /// <summary>Records the skip decided at launch; skips found during execution are recorded by <see cref="SkipAsync"/>.</summary>
    internal static Task ReportLaunchSkipAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        context.RuntimeState.DomainJoinSkipCode is { } code ? AppendSkipLogAsync(context, code, cancellationToken) : Task.CompletedTask;

    internal static async Task ConfirmEditionAsync(DeploymentStepExecutionContext context, string? edition, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(edition)) context.RuntimeState.ActualWindowsEditionId = edition;
        if (!PreOobeContentResolver.HasDomainTasks(context.Request, context.RuntimeState)) return;
        if (DomainJoinEditionRules.Evaluate(context.RuntimeState.ActualWindowsEditionId) == DomainJoinEditionSupport.Unsupported)
            await SkipAsync(context, DomainJoinExecutionStatus.SkippedUnsupportedEdition, DomainJoinSkipCode.UnsupportedEdition, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task SkipAsync(DeploymentStepExecutionContext context, DomainJoinExecutionStatus status, DomainJoinSkipCode code, CancellationToken cancellationToken)
    {
        context.RuntimeState.DomainJoinStatus = status;
        context.RuntimeState.DomainJoinSkipCode = code;
        context.ClearDomainJoinInput();
        await AppendSkipLogAsync(context, code, cancellationToken).ConfigureAwait(false);
    }

    private static Task AppendSkipLogAsync(DeploymentStepExecutionContext context, DomainJoinSkipCode code, CancellationToken cancellationToken) =>
        context.AppendLogAsync(DeploymentLogLevel.Warning, $"Domain joining skipped. Reason={code}.", cancellationToken);
}
