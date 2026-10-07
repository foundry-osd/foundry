// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Services.Deployment.Native;

/// <summary>
/// Supervises native mutations without terminating their cleanup when the caller cancels. Cancellation is signaled
/// through a named event so the worker stops at a safe point and still releases its native resources.
/// </summary>
internal sealed class NativeWorkerClient(IProcessRunner processRunner, string? executablePath = null)
{
    public async Task<NativeWorkerResult> ExecuteAsync(NativeWorkerRequest request, string workingDirectory, string operationName,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string executable = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The deployment executable path is unavailable.");
        Directory.CreateDirectory(workingDirectory);
        string requestPath = Path.Combine(workingDirectory, $"native-{Guid.NewGuid():N}.json");
        string? cancelEventName = cancellationToken.CanBeCanceled ? $@"Local\Foundry.Deploy.NativeCancel.{Guid.NewGuid():N}" : null;
        using EventWaitHandle? cancelEvent = cancelEventName is null ? null : new EventWaitHandle(false, EventResetMode.ManualReset, cancelEventName);
        object sync = new();
        NativeWorkerMessage? terminal = null;
        Exception? protocolFailure = null;
        Exception? progressFailure = null;
        int receivedLines = 0;

        void Receive(string line)
        {
            lock (sync)
            {
                receivedLines++;
                try
                {
                    NativeWorkerMessage message = JsonSerializer.Deserialize<NativeWorkerMessage>(line)
                        ?? throw new InvalidDataException("The native worker returned an empty event.");
                    if (terminal is not null) throw new InvalidDataException("The native worker returned events after completion.");
                    switch (message.Kind)
                    {
                        case "complete" when (message.Result is null) != (message.Error is null):
                            terminal = message;
                            break;
                        case "progress" when message.Result is null && message.Error is null &&
                                             message.Percent is double percent && double.IsFinite(percent) && percent is >= 0 and <= 100:
                            if (progressFailure is null)
                            {
                                try { progress?.Report(percent); }
                                catch (Exception exception) { progressFailure = exception; }
                            }
                            break;
                        default:
                            throw new InvalidDataException("The native worker returned an invalid event.");
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    protocolFailure ??= exception;
                }
            }
        }

        try
        {
            await using (var stream = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, request with { CancelEventName = cancelEventName },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();

            // Killing a servicing process can strand mounts or partially applied data. The event asks the worker to stop
            // at its next safe point; cancellation is observed only after the worker has released its native resources.
            using CancellationTokenRegistration cancelSignal = cancelEvent is null
                ? default
                : cancellationToken.Register(static state => ((EventWaitHandle)state!).Set(), cancelEvent);
            ProcessExecutionResult process = await processRunner.RunAsync(executable,
                [NativeDeploymentWorker.Command, requestPath], workingDirectory, Receive, null, CancellationToken.None).ConfigureAwait(false);
            if (receivedLines == 0)
            {
                foreach (string line in process.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) Receive(line);
            }
            if (terminal?.Error is NativeWorkerError error)
            {
                var native = new NativeOperationException(error.Function, error.ErrorCode, error.Message);
                native.CleanupErrors.AddRange(error.CleanupErrors);
                if (protocolFailure is not null) native.CleanupErrors.Add(protocolFailure.Message);
                string detail = $"{error.Function} failed (0x{unchecked((uint)error.ErrorCode):X8}): {error.Message}";
                if (native.CleanupErrors.Count > 0) detail += Environment.NewLine + string.Join(Environment.NewLine, native.CleanupErrors);
                if (cancellationToken.IsCancellationRequested)
                {
                    // The worker stopped because the caller asked it to; keep its native status as diagnostic context only.
                    Serilog.Log.ForContext<NativeWorkerClient>().Information(
                        "Native deployment operation stopped after cancellation. Operation={Operation}, Detail={Detail}", operationName, detail);
                    throw new OperationCanceledException(detail, native, cancellationToken);
                }
                throw new DeploymentOperationException(new(operationName, DeploymentFailureKinds.Process,
                    DeploymentFailureReasons.NonZeroExit, $"0x{unchecked((uint)error.ErrorCode):X8}"), detail, native);
            }
            if (protocolFailure is not null || terminal?.Result is null || !process.IsSuccess)
            {
                throw new DeploymentOperationException(DeploymentFailure.Guard(operationName,
                    DeploymentFailureReasons.InvalidPayload, "native_worker_protocol"),
                    $"The native deployment worker did not complete reliably (exit {process.ExitCode}).",
                    protocolFailure ?? new InvalidDataException("The native worker did not return a successful completion event."));
            }
            if (progressFailure is not null) ExceptionDispatchInfo.Capture(progressFailure).Throw();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100d);
            return terminal.Result;
        }
        finally
        {
            try { File.Delete(requestPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Serilog.Log.ForContext<NativeWorkerClient>().Warning(exception, "The native deployment request could not be removed. RequestPath={RequestPath}", requestPath);
            }
        }
    }
}
