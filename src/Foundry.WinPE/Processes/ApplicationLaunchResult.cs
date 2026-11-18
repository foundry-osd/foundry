// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Ui;

namespace Foundry.WinPE.Processes;

/// <summary>Reports process observations without manufacturing a child exception.</summary>
/// <param name="Succeeded">Whether the UI process exited with code 0.</param>
/// <param name="ExitCode">The exit code, or <see langword="null"/> when the process was still running or never started.</param>
/// <param name="ReadinessConfirmed">Whether the UI process reported <c>ui_ready</c> before it exited.</param>
/// <param name="FailureCategory">The <c>failure_category</c> of a launch that did not succeed.</param>
/// <param name="LastStage">The last startup stage the UI process reported.</param>
/// <param name="UiState">
/// The UI state file read once the process exited, or <see langword="null"/> when it is unknown (missing, unreadable or
/// invalid) or the process had not exited.
/// </param>
internal sealed record ApplicationLaunchResult(bool Succeeded, int? ExitCode = null,
    bool ReadinessConfirmed = false, string? FailureCategory = null, string? LastStage = null, UiState? UiState = null);
