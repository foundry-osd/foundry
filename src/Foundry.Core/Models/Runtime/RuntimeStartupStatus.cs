// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Runtime;

/// <summary>Identifies one child launch without carrying configuration, exception text, or secrets.</summary>
public sealed record RuntimeStartupStatus
{
    public int ProtocolVersion { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string LaunchId { get; init; } = string.Empty;
    public string Application { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string Stage { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string? FailureCategory { get; init; }
    /// <summary>
    /// Always <see langword="null"/>: the failure-record exchange it identified is gone, but the property stays so the status
    /// file keeps the shape that boot media of v26.10.18.1 read (the obsolete-media mode still writes it for them).
    /// </summary>
    public string? FailureRecordId { get; init; }
}
