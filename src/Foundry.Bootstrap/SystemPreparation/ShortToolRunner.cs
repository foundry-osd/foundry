// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Drains both output streams and terminates only the short-lived tool on timeout or cancellation.</summary>
internal sealed class ShortToolRunner : IShortToolRunner
{
    private const int MaximumCapturedCharacters = 4096;

    public async Task<ShortToolResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput, linked.Token);
        Task<string> standardError = ReadBoundedAsync(process.StandardError, linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            return new ShortToolResult(process.ExitCode, standardOutput.Result);
        }
        catch (OperationCanceledException exception)
        {
            await TerminateBestEffortAsync(process).ConfigureAwait(false);
            await ObserveBestEffortAsync(standardOutput, standardError).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                throw new TimeoutException($"{fileName} exceeded its execution deadline.", exception);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private static async Task ObserveBestEffortAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch
        {
            // Cleanup must not replace the primary cancellation or timeout.
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new char[MaximumCapturedCharacters];
        int captured = 0;
        var buffer = new char[512];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int copyLength = Math.Min(read, result.Length - captured);
            if (copyLength > 0)
            {
                Array.Copy(buffer, 0, result, captured, copyLength);
                captured += copyLength;
            }
        }

        return new string(result, 0, captured);
    }

    private static async Task TerminateBestEffortAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var waitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await process.WaitForExitAsync(waitDeadline.Token).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the cancellation or timeout that initiated termination.
        }
    }
}
