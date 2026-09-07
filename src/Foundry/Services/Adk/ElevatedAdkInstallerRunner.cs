// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using Foundry.Core.Services.Adk;
using Serilog;

namespace Foundry.Services.Adk;

/// <summary>Retains the waited UAC setup process until its actual exit, even after caller cancellation.</summary>
internal sealed class ElevatedAdkInstallerRunner(ILogger logger) : IAdkInstallerRunner
{
    private readonly ILogger logger = logger.ForContext<ElevatedAdkInstallerRunner>();
    private readonly object sync = new();
    private Task<AdkInstallerExecution>? activeOperation;
    private Process? retainedProcess;
    private AdkInstallerExecution? processIdentity;

    /// <inheritdoc />
    public Task? ActiveOperation { get { lock (sync) return activeOperation; } }

    /// <inheritdoc />
    public async Task<AdkInstallerExecution> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Task<AdkInstallerExecution> operation;
        lock (sync)
        {
            if (retainedProcess is not null || activeOperation is { IsCompleted: false }) throw new InvalidOperationException("An ADK installer is already active or requires recovery.");
            processIdentity = null;
            operation = activeOperation = Task.Run(() => StartAndWaitAsync(executablePath, arguments, cancellationToken), CancellationToken.None);
        }
        try
        {
            return await operation.WaitAsync(TimeSpan.FromHours(2), CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            lock (sync)
                return (processIdentity ?? new(null, false, false, true)) with { CancellationRequested = cancellationToken.IsCancellationRequested, OwnershipUncertain = true };
        }
    }

    private async Task<AdkInstallerExecution> StartAndWaitAsync(string path, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Process? process = null;
        int? processId = null;
        DateTimeOffset? started = null;
        bool exited = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path)! };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            process = Process.Start(start);
            if (process is null) return new(null, false, cancellationToken.IsCancellationRequested, true);
            processId = process.Id;
            started = process.StartTime.ToUniversalTime();
            lock (sync) processIdentity = new(null, false, false, false) { ProcessId = processId, StartTimeUtc = started };
            logger.Information("ADK setup process started. ExecutableName={ExecutableName}, ProcessId={ProcessId}, ProcessStartUtc={ProcessStartUtc}",
                Path.GetFileName(path), processId, started);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            exited = true;
            return new(process.ExitCode, true, cancellationToken.IsCancellationRequested, false) { ProcessId = processId, StartTimeUtc = started };
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223 && process is null)
        {
            return new(1602, true, true, false);
        }
        catch (OperationCanceledException error) when (process is null)
        {
            error.Data["ProcessRootExitConfirmed"] = true;
            error.Data["ProcessTreeTerminationConfirmed"] = true;
            throw;
        }
        catch (Exception) when (process is not null)
        {
            return new(null, false, cancellationToken.IsCancellationRequested, true) { ProcessId = processId, StartTimeUtc = started };
        }
        finally
        {
            if (exited) process?.Dispose();
            else if (process is not null) lock (sync) retainedProcess = process;
        }
    }
}
