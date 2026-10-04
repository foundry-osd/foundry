// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.PreOobe;

public enum PreOobeBuiltInKind { Driver, Network, Appx, AiRemoval, Activation, Cleanup, DomainJoinAndPlacement, VerifyDomainMembership }

/// <summary>Immutable installed-Windows input; its serialized bytes are bound to the seeded execution journal.</summary>
public sealed record PreOobeExecutionPlan
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int RuntimeContractVersion { get; init; } = 1;
    public string OperationId { get; init; } = string.Empty;
    public string DiagnosticSessionId { get; init; } = string.Empty;
    public string AttemptId { get; init; } = string.Empty;
    public IReadOnlyList<PreOobeExecutionAction> Actions { get; init; } = [];
    public IReadOnlyList<PreOobeStagedPackage> Packages { get; init; } = [];
    public IReadOnlyList<PreOobeOwnedPayload> OwnedPayloads { get; init; } = [];
}

/// <summary>Chooses exactly one custom action or runtime-owned built-in handler.</summary>
public sealed record PreOobeExecutionAction
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PreOobeActionSettings? CustomAction { get; init; }
    public PreOobeBuiltInKind? BuiltInKind { get; init; }
    /// <summary>Contains the selected built-in handler's versioned settings, validated by that handler.</summary>
    public JsonElement? Parameters { get; init; }
}

/// <summary>Registers an initially verified working copy. Its source hash does not describe later installer mutations.</summary>
public sealed record PreOobeStagedPackage
{
    public string ContentHash { get; init; } = string.Empty;
    /// <summary>Path relative to the installed Windows Temp/Foundry ownership root.</summary>
    public string RelativePath { get; init; } = string.Empty;
    public PreOobePackageManifest Manifest { get; init; } = new();
}

/// <summary>Registers disposal ownership separately from immutable file identity, including generated directory content.</summary>
public sealed record PreOobeOwnedPayload
{
    public string RelativePath { get; init; } = string.Empty;
    public bool IsSensitive { get; init; }
    public bool IsDirectory { get; init; }
    public IReadOnlyList<string> ConsumerActionIds { get; init; } = [];
}
