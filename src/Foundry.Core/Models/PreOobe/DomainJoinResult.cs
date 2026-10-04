// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.PreOobe;

/// <summary>Distinguishes observed success from failure, interrupted mutation and unverified membership.</summary>
public enum DomainJoinPhaseState { NotStarted, Succeeded, Failed, Skipped, Unknown, Unverified }

/// <summary>Tracks restart independently from OU placement success.</summary>
public enum DomainJoinRestartState { NotRequired, Required, Requested, Completed }

/// <summary>Tracks sensitive payload disposal independently from domain mutation outcomes.</summary>
public enum DomainJoinCleanupState { NotRequired, Pending, Disposed }

/// <summary>Allowlisted domain failures; diagnostics never persist account names or exception text.</summary>
public enum DomainJoinFailureCode
{
    InvalidInput, CredentialUnavailable, ContextMismatch, ComputerNameMismatch, DomainUnavailable,
    ReadinessTimeout, JoinFailed, PlacementFailed, MembershipMismatch, MembershipUnverified,
    Interrupted, WorkerTimeout, CleanupFailed, ResultUnavailable, InvalidResult
}

/// <summary>Reports one independent phase and an optional native or LDAP numeric diagnostic.</summary>
public sealed record DomainJoinPhaseResult
{
    [JsonRequired]
    public DomainJoinPhaseState State { get; init; } = DomainJoinPhaseState.NotStarted;
    public DomainJoinFailureCode? FailureCode { get; init; }
    public int? NativeErrorCode { get; init; }
    public int? LdapErrorCode { get; init; }
    /// <summary>Preserves an LDAP server result separately from transport/client and native error families.</summary>
    public int? DirectoryResultCode { get; init; }
}

/// <summary>Binds password-free results to one deployment attempt, immutable plan and originating boot.</summary>
public sealed record DomainJoinResult
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string OperationId { get; init; } = string.Empty;
    public string AttemptId { get; init; } = string.Empty;
    public string PlanHash { get; init; } = string.Empty;
    public string OriginatingBootId { get; init; } = string.Empty;
    public string ExpectedComputerName { get; init; } = string.Empty;
    public string ExpectedDomainName { get; init; } = string.Empty;
    public string? TargetOuDn { get; init; }
    public DomainJoinPhaseResult Join { get; init; } = new();
    public DomainJoinPhaseResult Placement { get; init; } = new();
    public DomainJoinPhaseResult Membership { get; init; } = new();
    public DomainJoinRestartState Restart { get; init; }
    public DomainJoinCleanupState Cleanup { get; init; }
    public Guid? ComputerObjectGuid { get; init; }
}
