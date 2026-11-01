// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

/// <summary>
/// Runs the Deploy-staged Foundry.AutopilotInteractive assistant while Windows Setup waits in specialize.
/// </summary>
/// <remarks>
/// Running from the specialize pass keeps the assistant available on every edition, including non-Enterprise editions
/// licensed by a firmware OEM key. The assistant is a staged self-contained executable that PostInstall starts directly.
/// It only uploads the hardware hash, so ending it on timeout cannot leave the installed system in a partial state.
/// The assistant may run for up to 120 minutes. PostInstall passes the absolute UTC deadline (now plus that timeout)
/// so the assistant stops gracefully 60 seconds before it; the process timeout remains the safety net.
/// </remarks>
public sealed class AutopilotRegistrationAction(string root, IPreOobeProcessExecutor processes,
    Func<DateTimeOffset>? utcNow = null)
{
    /// <summary>Bounds how long an unattended device can hold Windows Setup on the assistant.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(120);

    private readonly Func<DateTimeOffset> clock = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Maps the assistant's <see cref="AutopilotInteractiveExitCodes"/>; every outcome lets Setup continue.
    /// </summary>
    /// <remarks>
    /// <see cref="AutopilotInteractiveExitCodes.Registered"/> succeeds. <see cref="AutopilotInteractiveExitCodes.Skipped"/>
    /// succeeds with a warning and <c>autopilot_registration_skipped</c>, because the technician chose to skip. A timeout fails
    /// with <c>autopilot_registration_timed_out</c>; any other code, or a code from an uncertain termination, fails with
    /// <c>autopilot_registration_incomplete</c>.
    /// </remarks>
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken cancellationToken)
    {
        AutopilotRegistrationSettings settings = BuiltInSettings.Read<AutopilotRegistrationSettings>(action);
        string executable = OwnedPaths.Resolve(root, settings.ExecutablePath);
        string config = OwnedPaths.Resolve(root, settings.ConfigPath);
        if (!File.Exists(executable) || !File.Exists(config)) return new(false, FailureCode: "autopilot_assistant_missing");
        string output = OwnedPaths.Resolve(root, "Logs/PreOobe/" + action.Id + "/Process/output.log");
        string deadline = (clock() + Timeout).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        ProcessCommand command = new(executable, ["--config", config, "--deadline", deadline],
            Path.GetDirectoryName(executable)!, Timeout, OutputPath: output);
        ProcessOutcome result = await processes.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) return new(false, result.ExitCode, "autopilot_registration_timed_out");
        if (result.TerminationUncertain) return new(false, result.ExitCode, "autopilot_registration_incomplete");
        return result.ExitCode switch
        {
            AutopilotInteractiveExitCodes.Registered => new(true, result.ExitCode),
            AutopilotInteractiveExitCodes.Skipped => new(true, result.ExitCode, "autopilot_registration_skipped", HasWarnings: true),
            _ => new(false, result.ExitCode, "autopilot_registration_incomplete")
        };
    }
}
