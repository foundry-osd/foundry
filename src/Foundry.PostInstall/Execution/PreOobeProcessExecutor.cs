// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.PostInstall.Windows;

namespace Foundry.PostInstall.Execution;

public sealed class PreOobeProcessExecutor : IPreOobeProcessExecutor
{
    public const int MaximumOutputCharacters = 2 * 1024 * 1024;

    public async Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(command.Timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        using var child = SupervisedProcess.Start(command);
        Process process = child.Process;
        var output = new BoundedCapture();
        var errors = new BoundedCapture();
        Task stdout = DrainAsync(child.StandardOutput, output);
        Task stderr = DrainAsync(child.StandardError, errors);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Timeout);
        bool timedOut = false;
        bool uncertain = false;
        int? exitCode = null;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
            await child.WaitForChildrenAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            uncertain = true;
            child.Terminate();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            uncertain = true;
            child.Terminate();
        }
        try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // Inherited pipes can outlive the parent. They are not proof of completed work.
            uncertain = true;
            child.StandardOutput.Dispose();
            child.StandardError.Dispose();
        }
        string text = output.Snapshot();
        if (command.OutputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
            await File.WriteAllTextAsync(command.OutputPath, text + Environment.NewLine + errors.Snapshot(),
                new UTF8Encoding(false), CancellationToken.None).ConfigureAwait(false);
        }
        return new ProcessOutcome(exitCode, text, timedOut, uncertain, output.Truncated || errors.Truncated);
    }

    private static async Task DrainAsync(StreamReader reader, BoundedCapture capture)
    {
        char[] buffer = new char[4096];
        try
        {
            int length;
            while ((length = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0) capture.Append(buffer, length);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private sealed class BoundedCapture
    {
        private readonly StringBuilder _text = new();
        public bool Truncated { get; private set; }
        public void Append(char[] buffer, int count)
        {
            lock (_text)
            {
                int available = Math.Max(0, MaximumOutputCharacters - _text.Length);
                _text.Append(buffer, 0, Math.Min(available, count));
                Truncated |= count > available;
            }
        }
        public string Snapshot() { lock (_text) return _text.ToString(); }
    }
}
