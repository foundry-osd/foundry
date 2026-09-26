// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Records observed action outcomes without treating continuation after an error as success.</summary>
public sealed record PreOobeActionResult
{
    public string Status { get; init; } = "Pending";
    public int? ExitCode { get; init; }
    public string? FailureCode { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

/// <summary>Preserves the terminal sequence outcome independently from the Windows Setup process return code.</summary>
public sealed record PreOobeExecutionResult
{
    public int SchemaVersion { get; init; } = 1;
    public string OperationId { get; init; } = string.Empty;
    public string Status { get; init; } = "Pending";
    public IReadOnlyDictionary<string, PreOobeActionResult> Actions { get; init; } = new Dictionary<string, PreOobeActionResult>();
    public bool CleanupPending { get; init; }
}
