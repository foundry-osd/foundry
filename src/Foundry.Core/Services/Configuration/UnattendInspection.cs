// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Reports structural applicability and conservative compatibility signals without exposing XML values.
/// </summary>
public sealed record UnattendInspection
{
    /// <summary>
    /// Gets declared architectures in supported components.
    /// </summary>
    public IReadOnlyList<string> Architectures { get; init; } = [];

    /// <summary>
    /// Gets whether applicable components contain command settings requiring compatibility review.
    /// </summary>
    public bool HasCommands { get; init; }

    /// <summary>
    /// Gets whether applicable known settings take ownership of enrollment-sensitive OOBE or accounts.
    /// </summary>
    public bool ConflictsWithAutopilot { get; init; }

    /// <summary>Gets whether a join component exists, including empty and workgroup-only components.</summary>
    public bool HasUnattendedJoinComponent { get; init; }

    /// <summary>Gets exactly one valid applicable specialize name, preserving its original spelling.</summary>
    public string? ConcreteComputerName { get; init; }
}
