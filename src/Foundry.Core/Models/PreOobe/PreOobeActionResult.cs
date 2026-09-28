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
