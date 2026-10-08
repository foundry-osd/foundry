// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.PreOobe;

/// <summary>Started phases durably identify in-flight mutation, independently of terminal phase outcomes.</summary>
public enum DomainJoinReceiptPhase { Prepared, JoinStarted, JoinReturned, PlacementStarted, PlacementReturned, Finished }

/// <summary>Worker-owned receipt. Only an untouched Prepared seed may have an unassigned installed boot.</summary>
public sealed record DomainJoinPhaseReceipt
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public string OperationId { get; init; } = string.Empty;
    [JsonRequired]
    public string AttemptId { get; init; } = string.Empty;
    [JsonRequired]
    public string PlanHash { get; init; } = string.Empty;
    [JsonRequired]
    public string ActionId { get; init; } = string.Empty;
    [JsonRequired]
    public string? OriginatingBootId { get; init; }
    [JsonRequired]
    public long Generation { get; init; }
    [JsonRequired]
    public DomainJoinReceiptPhase Phase { get; init; }
    [JsonRequired]
    public DomainJoinPhaseResult Join { get; init; } = new();
    [JsonRequired]
    public DomainJoinPhaseResult Placement { get; init; } = new();
    [JsonRequired]
    public Guid? ComputerObjectGuid { get; init; }
    [JsonRequired]
    public Guid? DestinationObjectGuid { get; init; }
    [JsonRequired]
    public bool RestartRequired { get; init; }

    /// <summary>Creates the untouched receipt Deploy stages; the worker binds the installed boot before any mutation.</summary>
    public static DomainJoinPhaseReceipt CreateSeed(string operationId, string attemptId, string planHash, string actionId) => new()
    {
        OperationId = operationId,
        AttemptId = attemptId,
        PlanHash = planHash,
        ActionId = actionId
    };
}
