// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
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
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString());
            return 0;
        }
        var text = LocalizationText.Create();
        if (args is not (["--setup"] or ["--activation-worker"]))
        { Console.Error.WriteLine(text.GetString("PostInstall.Usage")); return 3; }
        try
        {
            if (!OperatingSystem.IsWindows() || !BootIdentityProvider.IsSetupContext())
            { Console.Error.WriteLine(text.GetString("PostInstall.SetupRequired")); return 3; }
            if (args[0] == "--activation-worker")
            {
                ActivationResult activation = new OemActivation(new WmiLicensing()).Run();
                Console.WriteLine(JsonSerializer.Serialize(activation, ExecutionJournal.JsonOptions));
                return activation.Failed ? 10 : 0;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string root = Path.Combine(windows, "Temp", "Foundry");
            OwnedPaths.RejectReparsePoints(windows, root);
            string path = OwnedPaths.Resolve(root, "State/PreOobe/plan.json");
            if (new FileInfo(path).Length > 8 * 1024 * 1024) return 3;
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var plan = JsonSerializer.Deserialize<PreOobeExecutionPlan>(bytes, ExecutionJournal.JsonOptions)
                ?? throw new InvalidDataException("The execution plan is empty.");
            text = LocalizationText.Create(plan.UiCulture);
            CultureInfo.CurrentUICulture = text.CurrentCulture;
            string logs = OwnedPaths.Resolve(root, "Logs/PreOobe");
            Directory.CreateDirectory(logs);
            Log.Logger = FoundryLogConfiguration.CreateFileLogger(Path.Combine(logs, "Foundry.PostInstall.log"), "Foundry.PostInstall",
                plan.DiagnosticSessionId, LogEventLevel.Verbose, 5);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Log.Information("Post-installation started with {ActionCount} actions", plan.Actions.Count);
            Console.WriteLine(text.GetString("PostInstall.Started"));
            var executor = new PreOobeActionExecutor(root, windows, plan, new PreOobeProcessExecutor(),
                new CertificateImporter(), Environment.ProcessPath ?? throw new InvalidDataException("Runtime path is unavailable."));
            OrchestrationOutcome result = await new PreOobeOrchestrator(root, hash, new ExecutionJournal(root), executor,
                BootIdentityProvider.Read).RunAsync(plan, CancellationToken.None).ConfigureAwait(false);
            Log.Information("Post-installation ended with {Status}; host exit code {HostExitCode}", result.Status, result.ExitCode);
            Console.WriteLine(text.GetString("PostInstall." + (result.Status is "Succeeded" or "CompletedWithErrors" or "AwaitingRestart" ? result.Status : "Failed")));
            return result.ExitCode;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Log.Error("Post-installation initialization failed; failure type {FailureType}", error.GetType().Name);
            Console.Error.WriteLine(text.GetString("PostInstall.InitializationFailed"));
            return 3;
        }
        finally { await Log.CloseAndFlushAsync().ConfigureAwait(false); }
    }
}
