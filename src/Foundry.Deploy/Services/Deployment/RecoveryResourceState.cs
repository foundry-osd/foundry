// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Describes discovered ownership of a recovery image or offline registry hive.</summary>
public enum RecoveryResourceState
{
    /// <summary>The exact native resource is confirmed absent.</summary>
    NotMounted,
    /// <summary>The exact native resource is present.</summary>
    Mounted,
    /// <summary>Discovery has not established the resource state.</summary>
    Unknown,
    /// <summary>Further mutation must wait for explicit reconciliation.</summary>
    RecoveryRequired
}

/// <summary>Identifies resources that must be retained and persisted by the deployment operation owner.</summary>
/// <remarks>Attached to the primary exception as FoundryRecoveryDiagnostic; it does not itself reconcile or release resources.</remarks>
public sealed record RecoveryResourceDiagnostic(RecoveryResourceState State, string ResourceKind,
    string ResourcePath, string? ImagePath, string Reason);
