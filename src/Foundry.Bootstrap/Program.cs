// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Foundry.Bootstrap.Console;
using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.IO;
using Foundry.Utilities.Runtime;
using Serilog;
using Serilog.Events;

namespace Foundry.Bootstrap;

internal static class Program
{
    private const string WinPeRoot = @"X:\Foundry";

    private static async Task<int> Main(string[] arguments)
    {
        if (arguments is ["--version"])
        {
            System.Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return 0;
        }
        if (arguments.Length != 0)
        {
            System.Console.WriteLine("Usage: Foundry.Bootstrap.exe [--help | --version]");
            System.Console.WriteLine("Without arguments, runs the boot workflow in Windows PE.");
            return arguments is ["--help"] ? 0 : 1;
        }

        string sessionId = DiagnosticSessionContext.CurrentSessionId;
        string? logPath = null;
        BootstrapLogPersistence? persistence = null;
        bool coordinatorStarted = false;
        long started = Stopwatch.GetTimestamp();
        var telemetry = new BootstrapTelemetry();
        using var presenter = new BootstrapConsole();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        System.Console.CancelKeyPress += cancel;
        try
        {
            try
            {
                logPath = WritableFilePathResolver.Resolve([Path.Combine(WinPeRoot, "Logs"),
                    Path.Combine(Path.GetTempPath(), "Foundry", "Logs")], "FoundryBootstrap.log");
                Log.Logger = FoundryLogConfiguration.CreateFileLogger(logPath, "Foundry.Bootstrap", sessionId,
                    LogEventLevel.Debug, 5, additionalSink: telemetry);
            }
            catch
            {
                logPath = null;
                Log.Logger = FoundryLogConfiguration.CreateDebugLogger("Foundry.Bootstrap", sessionId, LogEventLevel.Debug, additionalSink: telemetry);
            }

            if (!WinPeRuntimeDetector.IsWinPeRuntime())
            {
                Log.Warning("Bootstrap requires Windows PE; no boot operations were started");
                presenter.Report(new BootstrapProgress(BootstrapStage.Environment, BootstrapStatus.Failed,
                    "Foundry Bootstrap must run in Windows PE."));
                presenter.ShowDiagnostics(sessionId, logPath);
                return 1;
            }

            BootstrapVolume[] volumes = BootstrapEnvironment.EnumerateVolumes().ToArray();
            BootstrapVolume? cache = volumes.FirstOrDefault(volume => volume.IsReady &&
                string.Equals(volume.Label, "Foundry Cache", StringComparison.OrdinalIgnoreCase));
            telemetry.Configure(WinPeRoot, cache?.Root);
            persistence = new BootstrapLogPersistence(Path.GetDirectoryName(logPath) ?? Path.Combine(WinPeRoot, "Logs"),
                cache is null ? null : Path.Combine(cache.Root, "Logs", sessionId),
                Log.ForContext<BootstrapLogPersistence>());
            BootstrapContext context = BootstrapEnvironment.Create(WinPeRoot, sessionId,
                RuntimeInformation.OSArchitecture, volumes,
                path => File.Exists(path) ? File.ReadAllText(path) : null);
            Directory.CreateDirectory(context.RuntimeRoot);
            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var runtime = new RuntimeResolver(WinPeRoot, context.RuntimeRoot, context.RuntimeIdentifier,
                httpClient, Log.ForContext<RuntimeResolver>(), presenter.ReportDownload,
                warning: presenter.ReportWarning);
            var preparation = new WinPeSystemPreparation(WinPeRoot, httpClient,
                Log.ForContext<WinPeSystemPreparation>(), presenter.ReportWarning);
            var launcher = new ApplicationLauncher(Log.ForContext<ApplicationLauncher>(),
                context.PersistenceDirectory ?? Path.Combine(WinPeRoot, "Logs", sessionId),
                presenter.ReportWarning, telemetry.RecoverChildFailure);
            var coordinator = new BootstrapCoordinator(context, runtime, preparation, launcher, persistence,
                Log.ForContext<BootstrapCoordinator>(), presenter.Report,
                () => telemetry.StartDelivery(preparation.IsClockUsable), telemetry.Complete);
            coordinatorStarted = true;
            BootstrapResult result = await coordinator.RunAsync(cancellation.Token).ConfigureAwait(false);
            if (result.Outcome != BootstrapOutcome.Succeeded) { presenter.ShowDiagnostics(sessionId, logPath); }
            return result.ExitCode;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Bootstrap could not initialize");
            telemetry.Complete(new BootstrapResult(BootstrapOutcome.Failed, BootstrapStage.Environment), Stopwatch.GetElapsedTime(started));
            presenter.Report(new BootstrapProgress(BootstrapStage.Environment, BootstrapStatus.Failed,
                "The boot environment could not be initialized."));
            presenter.ShowDiagnostics(sessionId, logPath);
            return 1;
        }
        finally
        {
            System.Console.CancelKeyPress -= cancel;
            if (!coordinatorStarted && persistence is not null)
            {
                try { await persistence.PersistAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await Task.WhenAll(telemetry.ShutdownAsync(shutdown.Token), BootstrapLogShutdown.FlushAsync())
                    .WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch { }
        }
    }
}
