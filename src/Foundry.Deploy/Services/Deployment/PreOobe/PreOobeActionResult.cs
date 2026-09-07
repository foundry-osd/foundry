// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Records first-boot execution evidence; it never authorizes resuming destructive deployment.</summary>
public sealed record PreOobeActionResult(string Id, string Status, int? ExitCode, bool RebootRequired,
    int Attempt, DateTimeOffset StartedUtc, DateTimeOffset? CompletedUtc, string? ErrorCode);
