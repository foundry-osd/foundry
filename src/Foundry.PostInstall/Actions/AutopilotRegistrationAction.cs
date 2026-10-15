// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

/// <summary>
/// Runs the Deploy-staged interactive Autopilot registration assistant while Windows Setup waits in specialize.
/// </summary>
/// <remarks>
/// Running here instead of OOBE.cmd keeps the assistant available on non-Enterprise editions licensed by a firmware
/// OEM key, where windeploy skips user-provided setup scripts. The assistant only uploads the hardware hash, so ending
/// it on timeout cannot leave the installed system in a partial state.
/// </remarks>
public sealed class AutopilotRegistrationAction(string root, string windowsRoot, IPreOobeProcessExecutor processes)
{
    /// <summary>Bounds how long an unattended device can hold Windows Setup on the assistant.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(60);

    /// <summary>Succeeds only when the assistant reports a completed registration; every other end lets Setup continue.</summary>
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken cancellationToken)
    {
        AutopilotRegistrationSettings settings = BuiltInSettings.Read<AutopilotRegistrationSettings>(action);
        string script = OwnedPaths.Resolve(root, settings.ScriptPath);
        string config = OwnedPaths.Resolve(root, settings.ConfigPath);
        if (!File.Exists(script) || !File.Exists(config)) return new(false, FailureCode: "autopilot_assistant_missing");
        string output = OwnedPaths.Resolve(root, "Logs/PreOobe/" + action.Id + "/Process/output.log");
        ProcessCommand command = new(Path.Combine(windowsRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-STA", "-WindowStyle", "Hidden", "-File", script, "-ConfigPath", config],
            Path.GetDirectoryName(script)!, Timeout, OutputPath: output);
        ProcessOutcome result = await processes.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) return new(false, result.ExitCode, "autopilot_registration_timed_out");
        bool completed = result.ExitCode == 0 && !result.TerminationUncertain;
        return new(completed, result.ExitCode, completed ? null : "autopilot_registration_incomplete");
    }
}
