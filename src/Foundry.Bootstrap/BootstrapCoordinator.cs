// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Serilog;

namespace Foundry.Bootstrap;

/// <summary>
/// Runs the finite, ordered WinPE boot workflow. Every result carries the version of the payload resolved for the
/// current stage, or none when that stage has not resolved one yet. <paramref name="readPayloadVersion"/> defaults to
/// the executable file version and must not throw.
/// </summary>
/// <remarks>
/// The deployment key unlocked by Foundry Connect is relayed to Foundry Deploy through one
/// <see cref="IDeploymentKeyRelay"/> per boot (<paramref name="createKeyRelay"/>, default <see cref="DeploymentKeyRelay"/>).
/// Each child gets only its own pipe name variable, the other one is removed from its environment. The key is served only
/// when Connect exited 0, and the relay is disposed (key zeroed) when this workflow ends on any path. A hand-off failure is
/// logged with its exception type only and never changes the boot outcome; Deploy then prompts for the password.
/// </remarks>
internal sealed class BootstrapCoordinator(BootstrapContext context, IRuntimeResolver runtime,
    ISystemPreparation preparation, IApplicationLauncher launcher, IBootstrapLogPersistence persistence,
    ILogger logger, Action<BootstrapProgress> progress, Action? deliveryReady = null,
    Action<BootstrapResult, TimeSpan>? completed = null, Func<string, string?>? readPayloadVersion = null,
    Func<IDeploymentKeyRelay>? createKeyRelay = null)
{
    private const string HandoffNotRequested = "not_requested";
    private const string HandoffReceived = "received";
    private const string HandoffDelivered = "delivered";
    private const string HandoffNotDelivered = "not_delivered";

    private BootstrapStage stage = BootstrapStage.Environment;
    private BootstrapStage? loggedStage;
    private long stageStarted;
    private readonly Func<string, string?> readPayloadVersion = readPayloadVersion ?? ReadFileVersion;
    private readonly Func<IDeploymentKeyRelay> createKeyRelay = createKeyRelay ?? (() => new DeploymentKeyRelay(logger));
    private string? payloadVersion;
    // Logged once per boot: not_requested (no key from Connect, including a boot that ended before Connect ran), received
    // (key held, never served), delivered or not_delivered (served to Deploy).
    private string handoffOutcome = HandoffNotRequested;

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
            result = new BootstrapResult(BootstrapOutcome.Cancelled, stage, PayloadVersion: payloadVersion);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Bootstrap stage {Stage} failed", stage);
            result = new BootstrapResult(BootstrapOutcome.Failed, stage, PayloadVersion: payloadVersion);
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
                _ when result.ChildExitCode is int code => ChildExitMessages.Describe(result.Stage, code),
                _ when result.FailureCategory == "capability_invalid" => "Application startup metadata is invalid. Recreate the boot media or refresh the runtime cache.",
                _ => "This boot stage could not be completed. Check the session log for details."
            };
            Report(status, message);
            logger.Information("Deployment key hand-off finished. HandoffOutcome={HandoffOutcome}", handoffOutcome);
            logger.Information("Bootstrap finished with {Outcome} at {Stage}; child exit {ExitCode}; elapsed {DurationMilliseconds} ms",
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

        await BestEffortAsync(() => preparation.PrepareClockAsync(cancellationToken),
            "Early clock synchronization was unavailable. Continuing to Foundry Connect.").ConfigureAwait(false);

        stage = BootstrapStage.Connect;
        Report(BootstrapStatus.Running, "Preparing Foundry Connect");
        string connect = await runtime.ResolveAsync("Foundry.Connect", context.ConnectIsDebug, cancellationToken).ConfigureAwait(false);
        payloadVersion = readPayloadVersion(connect);

        // Disposal zeroes any held key on every exit path, including a Connect failure.
        using IDeploymentKeyRelay relay = createKeyRelay();
        string? sendPipe = TryBeginHandoff(relay.BeginReceive);
        bool handoffEnabled = sendPipe is not null;
        bool hasKey = false;
        bool receiveEnded = false;
        var connectEnvironment = new Dictionary<string, string?>(context.ChildEnvironment)
        {
            [DeploymentKeyHandoff.SendPipeEnvironmentVariable] = sendPipe,
            [DeploymentKeyHandoff.ReceivePipeEnvironmentVariable] = null
        };

        Report(BootstrapStatus.Running, "Waiting for Foundry Connect");
        ApplicationLaunchResult connectResult = await launcher.RunConnectAsync(connect, context.ConnectConfigurationPath,
            connectEnvironment, cancellationToken, handoffEnabled ? relay.ExpectSender : null,
            handoffEnabled ? EndReceiveAsync : null).ConfigureAwait(false);
        // The launcher returned without starting Connect (for example invalid startup metadata), so the hook never ran.
        if (handoffEnabled && !receiveEnded)
        {
            hasKey = await EndHandoffAsync(relay.EndReceiveAsync).ConfigureAwait(false);
        }
        if (hasKey) { handoffOutcome = HandoffReceived; }
        if (!connectResult.Succeeded)
        {
            return new BootstrapResult(connectResult.ExitCode == (int)ChildRuntimeExitCode.UserAborted ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed,
                stage, connectResult.ExitCode, connectResult.FailureCategory, connectResult.LastStage, PayloadVersion: payloadVersion);
        }
        Report(BootstrapStatus.Completed, "Foundry Connect completed");

        cancellationToken.ThrowIfCancellationRequested();
        stage = BootstrapStage.System;
        // A later failure must not be attributed to Connect; the next payload version is read once Deploy resolves.
        payloadVersion = null;
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
            await BestEffortAsync(() => runtime.RefreshAsync("Foundry.Connect", cancellationToken),
                "Foundry Connect could not be updated. Continuing.").ConfigureAwait(false);
        }

        string deploy = await runtime.ResolveAsync("Foundry.Deploy", context.DeployIsDebug, cancellationToken).ConfigureAwait(false);
        payloadVersion = readPayloadVersion(deploy);
        Report(BootstrapStatus.Running, "Preparing Foundry PostInstall");
        string postInstall = await runtime.ResolveAsync("Foundry.PostInstall", context.DeployIsDebug, cancellationToken).ConfigureAwait(false);
        var deployEnvironment = new Dictionary<string, string?>(context.ChildEnvironment)
        {
            [PostInstallRuntimeManifest.ExecutableEnvironmentVariable] = postInstall,
            [DeploymentKeyHandoff.SendPipeEnvironmentVariable] = null,
            [DeploymentKeyHandoff.ReceivePipeEnvironmentVariable] = null
        };
        // Debug media only: release media never carry this application, so Deploy downloads it into the target OS.
        if (context.DeployIsDebug && runtime.TryGetDebugArchive(AutopilotInteractiveLayout.ApplicationName) is var (archivePath, archiveSha256))
        {
            deployEnvironment[AutopilotInteractiveLayout.ArchiveEnvironmentVariable] = archivePath;
            deployEnvironment[AutopilotInteractiveLayout.ArchiveSha256EnvironmentVariable] = archiveSha256;
        }
        Report(BootstrapStatus.Completed, "Deployment applications prepared");
        stage = BootstrapStage.Deploy;
        Report(BootstrapStatus.Running, "Starting Foundry Deploy");
        cancellationToken.ThrowIfCancellationRequested();
        // The key is served only while the launcher waits for Deploy readiness; EndServeAsync zeroes it.
        string? receivePipe = hasKey ? TryBeginHandoff(relay.BeginServe) : null;
        deployEnvironment[DeploymentKeyHandoff.ReceivePipeEnvironmentVariable] = receivePipe;
        bool delivered = false;
        bool serveEnded = false;
        ApplicationLaunchResult deployResult;
        try
        {
            deployResult = await launcher.StartDeployAsync(deploy, deployEnvironment, cancellationToken,
                receivePipe is not null ? relay.ExpectReceiver : null,
                receivePipe is not null ? EndServeAsync : null).ConfigureAwait(false);
        }
        finally
        {
            if (handoffEnabled && !serveEnded)
            {
                delivered = await EndHandoffAsync(relay.EndServeAsync).ConfigureAwait(false);
            }
            if (receivePipe is not null) { handoffOutcome = delivered ? HandoffDelivered : HandoffNotDelivered; }
        }
        return new BootstrapResult(deployResult.Succeeded ? BootstrapOutcome.Succeeded : BootstrapOutcome.Failed, stage,
            deployResult.ExitCode, deployResult.FailureCategory, deployResult.LastStage, deployResult.ReadinessConfirmed, payloadVersion);

        // Launcher hooks: awaited while the launcher still holds the child's Process, so its process id cannot be reused.
        async Task EndReceiveAsync()
        {
            hasKey = await EndHandoffAsync(relay.EndReceiveAsync).ConfigureAwait(false);
            receiveEnded = true;
        }

        async Task EndServeAsync()
        {
            delivered = await EndHandoffAsync(relay.EndServeAsync).ConfigureAwait(false);
            serveEnded = true;
        }
    }

    private static string? ReadFileVersion(string executable)
    {
        try { return FileVersionInfo.GetVersionInfo(executable).FileVersion; }
        catch { return null; }
    }

    // The documented creation failures of a relay pipe; the child then gets no pipe name and Deploy prompts.
    private string? TryBeginHandoff(Func<string?> begin)
    {
        try { return begin(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or ObjectDisposedException)
        {
            LogHandoffFailure(exception);
            return null;
        }
    }

    // End* do not throw by contract; a failure still must not replace the boot outcome.
    private async Task<bool> EndHandoffAsync(Func<Task<bool>> end)
    {
        try { return await end().ConfigureAwait(false); }
        catch (Exception exception)
        {
            LogHandoffFailure(exception);
            return false;
        }
    }

    // Key hand-off code never attaches the exception object (its message is mirrored to remote logs).
    private void LogHandoffFailure(Exception exception) =>
        logger.Warning("Deployment key hand-off failed. FailureType={FailureType}", exception.GetType().Name);

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
        if (status == BootstrapStatus.Running && loggedStage != stage)
        {
            loggedStage = stage;
            stageStarted = Stopwatch.GetTimestamp();
            logger.Information("Bootstrap stage {Stage} started", stage);
        }
        if (status == BootstrapStatus.Completed)
        {
            logger.Information("Bootstrap stage {Stage} completed in {DurationMilliseconds:F0} ms",
                stage, Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        }
        progress(new BootstrapProgress(stage, status, message));
    }
}
