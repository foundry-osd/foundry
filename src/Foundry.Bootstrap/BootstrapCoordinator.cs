// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Serilog;

namespace Foundry.Bootstrap;

/// <summary>Runs the finite, ordered WinPE boot workflow.</summary>
internal sealed class BootstrapCoordinator(BootstrapContext context, IRuntimeResolver runtime,
    ISystemPreparation preparation, IApplicationLauncher launcher, IBootstrapLogPersistence persistence,
    ILogger logger, Action<BootstrapProgress> progress, Action? deliveryReady = null,
    Action<BootstrapResult, TimeSpan>? completed = null)
{
    private BootstrapStage stage = BootstrapStage.Environment;

    internal async Task<BootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        logger.Information("Bootstrap started in {DeploymentMode} mode for {RuntimeIdentifier}",
            context.IsUsb ? "Usb" : "Iso", context.RuntimeIdentifier);
        BootstrapResult result;
        try
        {
            result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new BootstrapResult(BootstrapOutcome.Cancelled, stage);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Bootstrap stage {Stage} failed", stage);
            result = new BootstrapResult(BootstrapOutcome.Failed, stage);
        }

        try
        {
            BootstrapStatus status = result.Outcome switch
            {
                BootstrapOutcome.Succeeded => BootstrapStatus.Completed,
                BootstrapOutcome.Cancelled => BootstrapStatus.Cancelled,
                _ => BootstrapStatus.Failed
            };
            string message = result.Outcome switch
            {
                BootstrapOutcome.Succeeded => result.ReadinessConfirmed ? "Foundry Deploy is ready." : "Foundry Deploy was launched. Readiness is unverified.",
                BootstrapOutcome.Cancelled => "Boot was cancelled. Deployment will not continue.",
                _ when result.FailureCategory == "readiness_timeout" => "Application readiness was not confirmed within two minutes. The application may still be running.",
                _ when result.ChildExitCode is int code => $"Foundry {(result.Stage == BootstrapStage.Connect ? "Connect" : "Deploy")} stopped with exit code {code}.",
                _ when result.FailureCategory == "capability_invalid" => "Application startup metadata is invalid. Recreate the boot media or refresh the runtime cache.",
                _ => "This boot stage could not be completed. Check the session log for details."
            };
            Report(status, message);
            logger.ForContext("RemoteDiagnostic", true).Information("Bootstrap finished with {Outcome} at {Stage}; child exit {ExitCode}; elapsed {DurationMilliseconds} ms",
                result.Outcome, result.Stage, result.ChildExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            try { completed?.Invoke(result, Stopwatch.GetElapsedTime(started)); }
            catch { }
        }
        finally
        {
            try { await persistence.PersistAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { logger.Warning(exception, "Boot log persistence failed"); }
        }

        return result;
    }

    private async Task<BootstrapResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(BootstrapStatus.Running, "Preparing network access");
        await BestEffortAsync(() => preparation.PrepareNetworkAsync(cancellationToken),
            "Some network preparation was unavailable. Continuing.").ConfigureAwait(false);
        Report(BootstrapStatus.Completed, "Environment prepared");

        stage = BootstrapStage.Connect;
        Report(BootstrapStatus.Running, "Preparing Foundry Connect");
        string connect;
        try
        {
            connect = await runtime.ResolveAsync("Foundry.Connect", true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !context.ConnectIsDebug)
        {
            logger.Warning(exception, "Provisioned Connect runtime unavailable; resolving release content");
            Report(BootstrapStatus.Warning, "Foundry Connect needs to be downloaded. Continuing.");
            connect = await runtime.ResolveAsync("Foundry.Connect", false, cancellationToken).ConfigureAwait(false);
        }

        Report(BootstrapStatus.Running, "Waiting for Foundry Connect");
        ApplicationLaunchResult connectResult = await launcher.RunConnectAsync(connect, context.ConnectConfigurationPath,
            context.ChildEnvironment, cancellationToken).ConfigureAwait(false);
        if (!connectResult.Succeeded)
        {
            return new BootstrapResult(connectResult.ExitCode == 20 ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed,
                stage, connectResult.ExitCode, connectResult.FailureCategory, connectResult.LastStage);
        }
        Report(BootstrapStatus.Completed, "Foundry Connect completed");

        cancellationToken.ThrowIfCancellationRequested();
        stage = BootstrapStage.System;
        Report(BootstrapStatus.Running, "Preparing the system clock and time zone");
        await BestEffortAsync(() => preparation.PrepareSystemAsync(cancellationToken),
            "Some system preparation was unavailable. Continuing.").ConfigureAwait(false);
        Report(BootstrapStatus.Completed, "System preparation completed");
        try { deliveryReady?.Invoke(); }
        catch { }

        stage = BootstrapStage.DeploymentPreparation;
        Report(BootstrapStatus.Running, "Preparing the deployment application");
        if (context.IsUsb && !context.ConnectIsDebug)
        {
            await BestEffortAsync(async () =>
                await runtime.ResolveAsync("Foundry.Connect", false, cancellationToken).ConfigureAwait(false),
                "Foundry Connect could not be updated. Continuing.").ConfigureAwait(false);
        }

        string deploy = await runtime.ResolveAsync("Foundry.Deploy", context.DeployIsDebug, cancellationToken).ConfigureAwait(false);
        Report(BootstrapStatus.Completed, "Deployment application prepared");
        stage = BootstrapStage.Deploy;
        Report(BootstrapStatus.Running, "Starting Foundry Deploy");
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationLaunchResult deployResult = await launcher.StartDeployAsync(deploy, context.ChildEnvironment, cancellationToken).ConfigureAwait(false);
        return new BootstrapResult(deployResult.Succeeded ? BootstrapOutcome.Succeeded : BootstrapOutcome.Failed, stage,
            deployResult.ExitCode, deployResult.FailureCategory, deployResult.LastStage, deployResult.ReadinessConfirmed);
    }

    private async Task BestEffortAsync(Func<Task> action, string warning)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(exception, "Optional boot preparation failed at {Stage}", stage);
            Report(BootstrapStatus.Warning, warning);
        }
    }

    private void Report(BootstrapStatus status, string message)
    {
        if (status == BootstrapStatus.Completed)
        {
            logger.ForContext("RemoteDiagnostic", true).Information("Bootstrap stage {Stage} completed", stage);
        }
        progress(new BootstrapProgress(stage, status, message));
    }
}
