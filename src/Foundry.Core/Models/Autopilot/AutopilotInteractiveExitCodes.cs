// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Autopilot;

/// <summary>
/// Process exit codes of <c>Foundry.AutopilotInteractive</c>: the contract between the application and the Foundry
/// PostInstall action that runs it during the specialize pass.
/// </summary>
/// <remarks>
/// PostInstall reads only the exit code, not the application's <c>result.json</c>. Every code lets Windows Setup continue;
/// the code decides how the PostInstall console and execution result report the action. A code PostInstall cannot trust,
/// because the process termination was uncertain, is reported as <see cref="Incomplete"/> whatever its value.
/// </remarks>
public static class AutopilotInteractiveExitCodes
{
    /// <summary>
    /// The device is registered: the upload completed, including when the profile assignment wait or the success countdown
    /// was ended early or reached the deadline. PostInstall reports the action as succeeded.
    /// </summary>
    public const int Registered = 0;

    /// <summary>
    /// The registration did not complete: the setup deadline arrived first, the configuration was missing or invalid, or the
    /// application failed. PostInstall reports the action as failed and continues.
    /// </summary>
    public const int Incomplete = 1;

    /// <summary>
    /// The technician confirmed "Skip registration" (the button or closing the window). PostInstall reports the action as
    /// succeeded with a warning, because skipping is a deliberate choice rather than a failure.
    /// </summary>
    public const int Skipped = 2;
}
