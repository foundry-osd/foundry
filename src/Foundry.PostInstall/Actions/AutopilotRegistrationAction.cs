// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

/// <summary>
/// Runs the Deploy-staged Autopilot registration assistant while Windows Setup waits in specialize.
/// </summary>
/// <remarks>
/// Running from the specialize pass keeps the assistant available on every edition, including non-Enterprise editions
/// licensed by a firmware OEM key. The assistant only uploads the hardware hash, so ending it on timeout cannot leave
/// the installed system in a partial state.
/// <para>
/// When the settings name an executable, PostInstall starts the staged self-contained Foundry.AutopilotInteractive
/// executable directly. It may run for up to 120 minutes; PostInstall passes the absolute UTC deadline (now plus that
/// timeout) so the assistant stops gracefully 60 seconds before it, and the process timeout remains the safety net.
/// </para>
/// <para>
/// When the settings name only a script, PostInstall runs the PowerShell assistant exactly as the v26.10.18.1 runtime did.
/// This is compatibility for boot media created before the media-pinned runtimes release, whose Deploy still stages the
/// script; remove it once those media are retired.
/// </para>
/// </remarks>
public sealed class AutopilotRegistrationAction(string root, string windowsRoot, IPreOobeProcessExecutor processes,
    Func<DateTimeOffset>? utcNow = null)
{
    /// <summary>Bounds how long an unattended device can hold Windows Setup on the assistant.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(120);

    /// <summary>Bounds the legacy PowerShell assistant, unchanged from the v26.10.18.1 runtime.</summary>
    public static readonly TimeSpan LegacyTimeout = TimeSpan.FromMinutes(60);

    private readonly Func<DateTimeOffset> clock = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Runs the executable when its path is set, otherwise the legacy script; with neither, fails with
    /// <c>autopilot_assistant_missing</c>. Every outcome lets Setup continue.
    /// </summary>
    /// <remarks>
    /// The executable's <see cref="AutopilotInteractiveExitCodes"/> are mapped as follows:
    /// <see cref="AutopilotInteractiveExitCodes.Registered"/> succeeds. <see cref="AutopilotInteractiveExitCodes.Skipped"/>
    /// succeeds with a warning and <c>autopilot_registration_skipped</c>, because the technician chose to skip. A timeout fails
    /// with <c>autopilot_registration_timed_out</c>; any other code, or a code from an uncertain termination, fails with
    /// <c>autopilot_registration_incomplete</c>. The legacy script succeeds only on exit code 0 without uncertain termination.
    /// </remarks>
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken cancellationToken)
    {
        AutopilotRegistrationSettings settings = BuiltInSettings.Read<AutopilotRegistrationSettings>(action);
        if (!string.IsNullOrWhiteSpace(settings.ExecutablePath))
            return await RunExecutableAsync(action, settings.ExecutablePath, settings.ConfigPath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(settings.ScriptPath))
            return await RunLegacyScriptAsync(action, settings.ScriptPath, settings.ConfigPath, cancellationToken).ConfigureAwait(false);
        return new(false, FailureCode: "autopilot_assistant_missing");
    }

    private async Task<ActionStepOutcome> RunExecutableAsync(PreOobeExecutionAction action, string executablePath, string configPath,
        CancellationToken cancellationToken)
    {
        string executable = OwnedPaths.Resolve(root, executablePath);
        string config = OwnedPaths.Resolve(root, configPath);
        if (!File.Exists(executable) || !File.Exists(config)) return new(false, FailureCode: "autopilot_assistant_missing");
        string deadline = (clock() + Timeout).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        ProcessCommand command = new(executable, ["--config", config, "--deadline", deadline],
            Path.GetDirectoryName(executable)!, Timeout, OutputPath: OutputPath(action));
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

    /// <summary>
    /// Compatibility for boot media created before the media-pinned runtimes release: runs the PowerShell assistant with the
    /// v26.10.18.1 command line. Remove once those media are retired.
    /// </summary>
    private async Task<ActionStepOutcome> RunLegacyScriptAsync(PreOobeExecutionAction action, string scriptPath, string configPath,
        CancellationToken cancellationToken)
    {
        string script = OwnedPaths.Resolve(root, scriptPath);
        string config = OwnedPaths.Resolve(root, configPath);
        if (!File.Exists(script) || !File.Exists(config)) return new(false, FailureCode: "autopilot_assistant_missing");
        ProcessCommand command = new(Path.Combine(windowsRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-STA", "-WindowStyle", "Hidden", "-File", script, "-ConfigPath", config],
            Path.GetDirectoryName(script)!, LegacyTimeout, OutputPath: OutputPath(action));
        ProcessOutcome result = await processes.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut) return new(false, result.ExitCode, "autopilot_registration_timed_out");
        bool completed = result.ExitCode == 0 && !result.TerminationUncertain;
        return new(completed, result.ExitCode, completed ? null : "autopilot_registration_incomplete");
    }

    private string OutputPath(PreOobeExecutionAction action) =>
        OwnedPaths.Resolve(root, "Logs/PreOobe/" + action.Id + "/Process/output.log");
}
