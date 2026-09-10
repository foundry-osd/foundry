// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Bootstrap.Runtime;

namespace Foundry.Bootstrap.Console;

/// <summary>Shows durable stage lines and bounded progress updates on basic WinPE consoles.</summary>
internal sealed class BootstrapConsole : IDisposable
{
    private readonly object gate = new();
    private readonly Timer heartbeat;
    private BootstrapProgress? current;
    private long stageStarted = Stopwatch.GetTimestamp();
    private long lastOutput = Stopwatch.GetTimestamp();
    private bool stopped;

    internal BootstrapConsole()
    {
        WriteLine("Foundry Bootstrap");
        foreach (BootstrapStage stage in Enum.GetValues<BootstrapStage>())
        {
            WriteLine($"[{(int)stage + 1}/5] {StageName(stage)}: Pending");
        }
        heartbeat = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    internal void Report(BootstrapProgress value)
    {
        lock (gate)
        {
            if (current?.Stage != value.Stage) { stageStarted = Stopwatch.GetTimestamp(); }
            current = value;
            WriteLine($"[{(int)value.Stage + 1}/5] {StageName(value.Stage)}: {value.Status} - {value.Message}");
            lastOutput = Stopwatch.GetTimestamp();
            if (value.Status is BootstrapStatus.Failed or BootstrapStatus.Cancelled ||
                value.Stage == BootstrapStage.Deploy && value.Status == BootstrapStatus.Completed)
            {
                stopped = true;
            }
        }
    }

    internal void ReportDownload(RuntimeDownloadProgress value)
    {
        lock (gate)
        {
            if (stopped || Stopwatch.GetElapsedTime(lastOutput) < TimeSpan.FromSeconds(1)) { return; }
            string total = value.TotalBytes is > 0
                ? $" / {value.TotalBytes.Value / 1048576d:F1} MB ({Math.Min(100, 100d * value.BytesReceived / value.TotalBytes.Value):F0}%)"
                : "";
            WriteLine($"Downloading {value.ApplicationName}: {value.BytesReceived / 1048576d:F1} MB{total}");
            lastOutput = Stopwatch.GetTimestamp();
        }
    }

    internal void ReportWarning(string message)
    {
        lock (gate)
        {
            if (!stopped && current is not null)
            {
                Report(new BootstrapProgress(current.Stage, BootstrapStatus.Warning, message));
            }
        }
    }

    internal void ShowDiagnostics(string sessionId, string? logPath)
    {
        lock (gate)
        {
            WriteLine($"Diagnostic session: {sessionId}");
            WriteLine(logPath is null ? "A diagnostic log file could not be opened." : $"Log: {logPath}");
        }
    }

    private void Refresh()
    {
        lock (gate)
        {
            if (stopped || current is null || current.Status == BootstrapStatus.Completed ||
                Stopwatch.GetElapsedTime(lastOutput) < TimeSpan.FromSeconds(15)) { return; }
            WriteLine($"{current.Message} - elapsed {Stopwatch.GetElapsedTime(stageStarted):mm\\:ss}");
            lastOutput = Stopwatch.GetTimestamp();
        }
    }

    internal static string StageName(BootstrapStage stage) => stage switch
    {
        BootstrapStage.Environment => "Prepare environment",
        BootstrapStage.Connect => "Start Connect",
        BootstrapStage.System => "Prepare system",
        BootstrapStage.DeploymentPreparation => "Prepare deployment application",
        BootstrapStage.Deploy => "Start Deploy",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static void WriteLine(string message)
    {
        try { System.Console.WriteLine(message); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (gate) { stopped = true; }
        heartbeat.Dispose();
    }
}
