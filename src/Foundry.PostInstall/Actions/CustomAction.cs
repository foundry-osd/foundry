// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

public sealed class CustomAction(string root, string windowsRoot, PreOobeExecutionPlan plan, IPreOobeProcessExecutor processes)
{
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken cancellationToken)
    {
        PreOobeActionSettings settings = action.CustomAction ?? throw new InvalidDataException("Custom action is missing.");
        PreOobeProcessSettings policy = settings.Process ?? throw new InvalidDataException("Process policy is missing.");
        if (settings.ApplicationMode == PreOobeApplicationMode.Msi && !PreOobeConfigurationValidator.AreMsiArgumentsSafe(settings.Arguments))
            throw new InvalidDataException("MSI arguments conflict with controlled restart behavior.");
        if (policy.TimeoutSeconds is < 1 or > 86400 ||
            !Enum.IsDefined(policy.ErrorPolicy) || !Enum.IsDefined(policy.RestartTiming) ||
            policy.SuccessExitCodes.Intersect(policy.RestartExitCodes).Any() ||
            policy.SuccessExitCodes.Contains(1641) || policy.RestartExitCodes.Contains(1641))
            throw new InvalidDataException("Process policy is invalid.");
        string work = OwnedPaths.Resolve(root, $"Work/PreOobe/{plan.OperationId}/{action.Id}");
        Directory.CreateDirectory(work);
        string? packageRoot = null;
        if (settings.Package is not null)
        {
            PreOobeStagedPackage package = plan.Packages.SingleOrDefault(item => item.ContentHash == settings.Package.ContentHash)
                ?? throw new InvalidDataException("Staged package is missing.");
            packageRoot = OwnedPaths.Resolve(root, package.RelativePath);
        }
        string directory = packageRoot ?? work;
        if (settings.WorkingDirectory is not null) directory = OwnedPaths.Resolve(directory, settings.WorkingDirectory);
        if (!Directory.Exists(directory)) return new(false, FailureCode: "working_directory_missing");
        string system = Path.Combine(windowsRoot, "System32");
        TimeSpan timeout = TimeSpan.FromSeconds(policy.TimeoutSeconds);
        string logs = OwnedPaths.Resolve(root, "Logs/PreOobe/" + action.Id);
        Directory.CreateDirectory(logs);
        string output = Path.Combine(logs, "process-output.log");
        ProcessCommand command;
        if (settings.Kind == PreOobeActionKind.Command)
        {
            if (string.IsNullOrWhiteSpace(settings.Command) || settings.Command.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new InvalidDataException("Command is invalid.");
            command = new(Path.Combine(system, "cmd.exe"), [], directory, timeout,
                RawArguments: "/d /s /c \"" + settings.Command + "\"", OutputPath: output);
        }
        else
        {
            string entry = OwnedPaths.Resolve(packageRoot ?? throw new InvalidDataException("Package is required."),
                settings.EntryPoint ?? throw new InvalidDataException("Entry point is required."));
            if (!File.Exists(entry)) return new(false, FailureCode: "entry_point_missing");
            if (settings.Arguments?.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new InvalidDataException("Arguments are invalid.");
            command = settings.Kind switch
            {
                PreOobeActionKind.PowerShell when Path.GetExtension(entry).Equals(".ps1", StringComparison.OrdinalIgnoreCase) =>
                    new(Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
                        ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry], directory, timeout, settings.Arguments, output),
                PreOobeActionKind.Application when settings.ApplicationMode == PreOobeApplicationMode.Msi &&
                    Path.GetExtension(entry).Equals(".msi", StringComparison.OrdinalIgnoreCase) =>
                    new(Path.Combine(system, "msiexec.exe"), ["/i", entry, "/qn", "/norestart", "/l*v", Path.Combine(logs, "msi.log")],
                        directory, timeout, settings.Arguments + " REBOOT=ReallySuppress /qn /norestart", output),
                PreOobeActionKind.Application when settings.ApplicationMode == PreOobeApplicationMode.Exe &&
                    Path.GetExtension(entry).Equals(".exe", StringComparison.OrdinalIgnoreCase) =>
                    new(entry, [], directory, timeout, settings.Arguments, output),
                _ => throw new InvalidDataException("Unsupported custom action.")
            };
        }
        ProcessOutcome result;
        try { result = await processes.RunAsync(command, cancellationToken).ConfigureAwait(false); }
        catch (System.ComponentModel.Win32Exception) { return new(false, FailureCode: "process_start_failed"); }
        bool uncertain = result.TerminationUncertain || result.TimedOut || result.ExitCode is null or 1641;
        bool restart = result.ExitCode is int code && policy.RestartExitCodes.Contains(code);
        bool success = !uncertain && (restart || result.ExitCode is int exit && policy.SuccessExitCodes.Contains(exit));
        return new(success, result.ExitCode, success ? null : uncertain ? "execution_uncertain" : "process_exit_code",
            restart && success, uncertain);
    }
}
