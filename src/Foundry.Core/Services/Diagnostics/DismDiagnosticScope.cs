// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Utilities.Processes;

namespace Foundry.Core.Services.Diagnostics;

/// <summary>Retains bounded DISM console evidence in the current operation's explicit diagnostic directory.</summary>
/// <remarks>It never reads the machine-wide DISM log. Capture failure cannot replace a native operation outcome.</remarks>
public sealed class DismDiagnosticScope : IDisposable
{
    private const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly AsyncLocal<DismDiagnosticScope?> Current = new();
    private readonly DismDiagnosticScope? previous;
    private readonly object sync = new();
    private bool started;
    private bool disabled;
    private bool disposed;

    public DismDiagnosticScope(string directory)
    {
        LogPath = Path.Combine(Path.GetFullPath(directory), "Foundry.Dism.log");
        try
        {
            RejectReparsePoints(LogPath);
            if (File.Exists(LogPath)) File.WriteAllBytes(LogPath, []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { disabled = true; }
        previous = Current.Value;
        Current.Value = this;
    }

    public string LogPath { get; }
    public string? CapturedLogPath => started ? LogPath : null;

    /// <summary>Captures already-bounded process output only for DISM in the active operation.</summary>
    public static void Record(string executable, ProcessExecutionResult result)
    {
        if (Path.GetFileName(executable).Equals("dism.exe", StringComparison.OrdinalIgnoreCase))
            Current.Value?.Append(result);
    }

    private void Append(ProcessExecutionResult result)
    {
        lock (sync)
        {
            if (disposed || disabled) return;
            try
            {
                RejectReparsePoints(LogPath);
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RejectReparsePoints(LogPath);
                using var stream = new FileStream(LogPath, started ? FileMode.OpenOrCreate : FileMode.Create,
                    FileAccess.Write, FileShare.Read);
                started = true;
                stream.Seek(0, SeekOrigin.End);
                string text = $"{DateTimeOffset.UtcNow:O} DISM exit={result.ExitCode}; outputTruncated={result.StandardOutputTruncated || result.StandardErrorTruncated}{Environment.NewLine}" +
                    result.StandardOutput + Environment.NewLine + result.StandardError + Environment.NewLine;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                int remaining = (int)Math.Max(0, MaximumBytes - stream.Length);
                int count = Math.Min(bytes.Length, remaining);
                while (count > 0 && count < bytes.Length && (bytes[count] & 0xC0) == 0x80) count--;
                stream.Write(bytes, 0, count);
                if (bytes.Length >= remaining) disabled = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { disabled = true; }
        }
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Diagnostic paths cannot traverse reparse points.");
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(Current.Value, this)) Current.Value = previous;
        }
    }
}
