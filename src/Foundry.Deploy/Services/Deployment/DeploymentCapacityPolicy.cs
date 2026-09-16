// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Shares layout reservations and budgets known simultaneous target residents without estimating unknown driver expansion.</summary>
internal static class DeploymentCapacityPolicy
{
    public const int EfiPartitionSizeMb = 260;
    public const int MsrPartitionSizeMb = 16;
    public const int RecoveryPartitionSizeMb = 5120;
    public const long AlignmentAllowanceBytes = 4L * 1024 * 1024;
    public const long ScratchAndHeadroomBytes = 1024L * 1024 * 1024;
    public const long LayoutReserveBytes = (EfiPartitionSizeMb + MsrPartitionSizeMb + RecoveryPartitionSizeMb) * 1024L * 1024 + AlignmentAllowanceBytes;

    /// <summary>Includes known archive copies and optional setup-media expansion; arithmetic overflow is invalid metadata.</summary>
    internal static long RequiredWindowsBytes(WindowsImageMetadata? image, long targetArchiveBytes, long targetDriverBytes,
        bool needsOptionalFeatureSource)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(targetDriverBytes);
        return checked((image?.SizeBytes ?? 0) + targetArchiveBytes + targetDriverBytes + ScratchAndHeadroomBytes +
            (needsOptionalFeatureSource ? image?.SetupMediaSizeBytes ?? 0 : 0));
    }

    /// <summary>Rejects known requirements exceeding the confirmed disk, including shared layout and alignment reserves.</summary>
    internal static void EnsureTargetCapacity(DeploymentStepExecutionContext context, WindowsImageMetadata? image,
        long targetArchiveBytes, long targetDriverBytes)
    {
        long required = checked(LayoutReserveBytes + RequiredWindowsBytes(image, targetArchiveBytes, targetDriverBytes,
            NeedsOptionalFeatureSource(context)));
        ulong knownSize = context.Request.TargetDiskIdentity?.SizeBytes ?? 0;
        if (knownSize > 0 && (ulong)required > knownSize)
        {
            throw Failure();
        }
    }

    /// <summary>Reserves matching source media only for enabled features whose effective catalog entry can require it.</summary>
    internal static bool NeedsOptionalFeatureSource(DeploymentStepExecutionContext context) =>
        context.Request.WindowsOptionalFeatures.IsEnabled && context.Request.WindowsOptionalFeatures.Actions.Any(action =>
            action.Enable && WindowsOptionalFeatureCatalog.GetEffectiveEntry(action.Id)?.RequiresSetupMediaSxs == true);

    internal static DeploymentOperationException Failure() => new(
        DeploymentFailure.Guard(DeploymentOperationNames.PreflightDeployment, DeploymentFailureReasons.InvalidInput, "insufficient_target_capacity"),
        Foundry.Deploy.Services.Localization.LocalizationText.GetString("Preflight.InsufficientCapacity"));
}
