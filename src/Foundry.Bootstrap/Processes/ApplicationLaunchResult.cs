// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Processes;

/// <summary>Reports process observations without manufacturing a child exception.</summary>
internal sealed record ApplicationLaunchResult(bool Succeeded, int? ExitCode = null,
    bool ReadinessConfirmed = false, string? FailureCategory = null, string? LastStage = null);
