// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;

namespace Foundry.Core.Services.Adk;

/// <summary>Runs an elevated ADK bundle while retaining ownership until the installer exits.</summary>
public sealed class AdkSetupRunner
{
    private readonly Func<ProcessStartInfo, IAdkSetupProcess?> startProcess;

    /// <summary>Uses the Windows shell to request elevation for setup.</summary>
    public AdkSetupRunner() : this(StartProcess)
    {
    }

    /// <summary>Separates native process creation from the setup orchestration policy.</summary>
    internal AdkSetupRunner(Func<ProcessStartInfo, IAdkSetupProcess?> startProcess)
    {
        this.startProcess = startProcess;
    }

    /// <summary>Returns a successful setup code or throws a classified failure. Cancellation after launch is deferred until exit.</summary>
    public async Task<int> RunAsync(string setupPath, string arguments, string logPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(setupPath))
        {
            throw new AdkSetupException("setup_not_found", setupPath, logPath);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new AdkSetupException("log_preparation_failed", setupPath, logPath, innerException: exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        IAdkSetupProcess? started;
        try
        {
            started = startProcess(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = $"{arguments} /log \"{logPath}\"",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception exception)
        {
            string reason = exception.NativeErrorCode switch
            {
                1223 => "elevation_cancelled",
                2 or 3 => "setup_not_found",
                _ => "launch_failed",
            };
            throw new AdkSetupException(reason, setupPath, logPath, nativeErrorCode: exception.NativeErrorCode, innerException: exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new AdkSetupException("setup_not_found", setupPath, logPath, innerException: exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            throw new AdkSetupException("launch_failed", setupPath, logPath, innerException: exception);
        }

        if (started is null)
        {
            throw new AdkSetupException("process_not_started", setupPath, logPath);
        }

        using (started)
        {
            // Cancelling the wait would release the caller's operation lock and cache lease while setup still uses them.
            await started.WaitForExitAsync().ConfigureAwait(false);
            int exitCode = started.ExitCode;
            if (exitCode is not (0 or 3010))
            {
                throw new AdkSetupException("installer_exit_failed", setupPath, logPath, exitCode: exitCode);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return exitCode;
        }
    }

    private static IAdkSetupProcess? StartProcess(ProcessStartInfo startInfo)
    {
        Process? process = Process.Start(startInfo);
        return process is null ? null : new SetupProcess(process);
    }

    private sealed class SetupProcess(Process process) : IAdkSetupProcess
    {
        public int ExitCode => process.ExitCode;
        public Task WaitForExitAsync() => process.WaitForExitAsync(CancellationToken.None);
        public void Dispose() => process.Dispose();
    }
}

/// <summary>Owns a started setup process until its exit can be observed.</summary>
internal interface IAdkSetupProcess : IDisposable
{
    /// <summary>Gets the result after the installer exits.</summary>
    int ExitCode { get; }

    /// <summary>Waits without cancellation so an active installer never outlives its operation ownership.</summary>
    Task WaitForExitAsync();
}
