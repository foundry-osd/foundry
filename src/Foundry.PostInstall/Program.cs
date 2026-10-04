// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Console;
using Foundry.PostInstall.Execution;
using Foundry.PostInstall.Windows;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.PostInstall;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--version"])
        {
            System.Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString());
            return 0;
        }
        if (args is not (["--setup"] or ["--activation-worker"] or ["--domain-join-worker"]))
        { WriteError("Foundry.PostInstall --setup | --version"); return 3; }
        try
        {
            if (!OperatingSystem.IsWindows() || !BootIdentityProvider.IsSetupContext())
            { WriteError("This application must be started by Windows Setup as Local System."); return 3; }
            if (args[0] == "--activation-worker")
            {
                ActivationResult activation = new OemActivation(new WmiLicensing()).Run();
                System.Console.WriteLine(JsonSerializer.Serialize(activation, ExecutionJournal.JsonOptions));
                return activation.Failed ? 10 : 0;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string root = Path.Combine(windows, "Temp", "Foundry");
            OwnedPaths.RejectReparsePoints(windows, root);
            var snapshot = await PreOobePlanLoader.LoadAsync(root, CancellationToken.None).ConfigureAwait(false);
            var plan = snapshot.Plan;
            string hash = snapshot.Hash;
            if (args[0] == "--domain-join-worker")
            {
                var binding = DomainJoinBinding.Validate(plan);
                string boot = BootIdentityProvider.Read();
                DomainJoinBinding.RequireRunning(root, plan, hash, boot, binding.JoinAction.Id);
                var workerResult = await new DomainJoinWorker(root, plan, hash, boot, new NativeDomainJoin(), new DomainComputerAccountDirectory())
                    .RunAsync(binding.Parameters, CancellationToken.None).ConfigureAwait(false);
                return workerResult.Join.State == DomainJoinPhaseState.Succeeded ? 0 : 10;
            }
            string logs = OwnedPaths.Resolve(root, "Logs/PreOobe");
            Directory.CreateDirectory(logs);
            Log.Logger = FoundryLogConfiguration.CreateFileLogger(Path.Combine(logs, "Foundry.PostInstall.log"), "Foundry.PostInstall",
                plan.DiagnosticSessionId, LogEventLevel.Verbose, 5);
            Log.Information("Post-installation started with {ActionCount} actions", plan.Actions.Count);
            using var console = new PostInstallConsole(plan, Path.Combine(logs, "Foundry.PostInstall.log"));
            var executor = new PreOobeActionExecutor(root, windows, plan, new PreOobeProcessExecutor(),
                new CertificateImporter(), Environment.ProcessPath ?? throw new InvalidDataException("Runtime path is unavailable."), hash);
            OrchestrationOutcome result = await new PreOobeOrchestrator(root, hash, new ExecutionJournal(root), executor,
                BootIdentityProvider.Read, console).RunAsync(plan, CancellationToken.None).ConfigureAwait(false);
            Log.Information("Post-installation ended with {Status}; host exit code {HostExitCode}", result.Status, result.ExitCode);
            console.Complete(result);
            await console.WaitForSetupAsync().ConfigureAwait(false);
            return result.ExitCode;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Log.Error("Post-installation initialization failed; failure type {FailureType}", error.GetType().Name);
            WriteError("Post-installation could not initialize. Check the staged plan, journal, and runtime files.");
            return 3;
        }
        finally { await Log.CloseAndFlushAsync().ConfigureAwait(false); }
    }

    private static void WriteError(string message)
    {
        try { System.Console.Error.WriteLine(message); }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException) { }
    }
}
