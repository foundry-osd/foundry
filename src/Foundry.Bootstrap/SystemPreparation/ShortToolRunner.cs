// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Drains both output streams and terminates only the short-lived tool on timeout or cancellation.</summary>
internal sealed class ShortToolRunner : IShortToolRunner
{
    public async Task<int> RunAsync(
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
        Task standardOutput = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, linked.Token);
        Task standardError = process.StandardError.BaseStream.CopyToAsync(Stream.Null, linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException exception)
        {
            await TerminateBestEffortAsync(process).ConfigureAwait(false);
            await ObserveBestEffortAsync(standardOutput, standardError).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                throw new TimeoutException($"{fileName} exceeded its execution deadline.", exception);
            }

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
