// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DomainJoin;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentLaunchPreparationResult : IDisposable
{
    private DomainJoinPreparedInput? domainJoinInput;

    /// <summary>Transfers credential ownership once; otherwise disposal clears the unconsumed input.</summary>
    public DomainJoinPreparedInput? TakeDomainJoinInput() => Interlocked.Exchange(ref domainJoinInput, null);
    public void Dispose() => TakeDomainJoinInput()?.Dispose();
    public string? FailureMessage { get; init; }
    public required bool IsReadyToStart { get; init; }
    public required string NormalizedComputerName { get; init; }
    public TargetDiskInfo? EffectiveTargetDisk { get; init; }
    public DeploymentContext? Context { get; init; }

    public static DeploymentLaunchPreparationResult Failure(string normalizedComputerName, string? failureMessage = null)
    {
        return new DeploymentLaunchPreparationResult
        {
            FailureMessage = failureMessage,
            IsReadyToStart = false,
            NormalizedComputerName = normalizedComputerName
        };
    }

    public static DeploymentLaunchPreparationResult Success(
        string normalizedComputerName,
        TargetDiskInfo effectiveTargetDisk,
        DeploymentContext context,
        DomainJoinPreparedInput? domainJoinInput = null)
    {
        return new DeploymentLaunchPreparationResult
        {
            IsReadyToStart = true,
            NormalizedComputerName = normalizedComputerName,
            EffectiveTargetDisk = effectiveTargetDisk,
            Context = context,
            domainJoinInput = domainJoinInput
        };
    }
}
