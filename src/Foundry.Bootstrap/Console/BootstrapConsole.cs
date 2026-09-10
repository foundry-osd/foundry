// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Bootstrap.Runtime;

namespace Foundry.Bootstrap.Console;

/// <summary>Maintains a compact WinPE progress screen, with plain output when cursor control is unavailable.</summary>
internal sealed class BootstrapConsole : IDisposable
{
    private readonly object gate = new();
    private readonly TextWriter output;
    private readonly Timer heartbeat;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly BootstrapStatus[] stages = new BootstrapStatus[5];
    private readonly long?[] stageStarts = new long?[5];
    private readonly TimeSpan?[] stageDurations = new TimeSpan?[5];
    private readonly List<(BootstrapStage Stage, string Message)> warnings = [];
    private BootstrapProgress? current;
    private BootstrapResult? result;
    private RuntimeDownloadProgress? download;
    private string? activity;
    private string? diagnosticSession;
    private string? diagnosticLog;
    private long lastOutput = Stopwatch.GetTimestamp();
    private TimeSpan? elapsed;
    private bool stopped;
    private bool disposed;
    private bool interactive;
    private bool? originalCursorVisible;
    private ConsoleColor originalColor;
    private int paintedRows;

    // Supplying a writer uses the same sequential fallback as redirected console output.
    internal BootstrapConsole(TextWriter? output = null)
    {
        this.output = output ?? System.Console.Out;
        if (output is null)
        {
            try
            {
                if (!System.Console.IsOutputRedirected)
                {
                    originalColor = System.Console.ForegroundColor;
                    System.Console.Clear();
                    originalCursorVisible = System.Console.CursorVisible;
                    System.Console.CursorVisible = false;
                    interactive = true;
                }
            }
            catch (Exception exception) when (IsConsoleFailure(exception)) { RestoreConsole(); }
        }
        if (!Render()) WriteLine("Foundry Bootstrap");
        heartbeat = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    internal void Report(BootstrapProgress value)
    {
        lock (gate)
        {
            if (stopped || disposed) return;
            if (value.Status == BootstrapStatus.Warning)
            {
                AddWarning(value.Stage, value.Message);
                return;
            }
            current = value;
            download = null;
            activity = null;
            int index = (int)value.Stage;
            if (value.Status == BootstrapStatus.Running) stageStarts[index] ??= Stopwatch.GetTimestamp();
            else if (stageStarts[index] is long stageStart)
                stageDurations[index] ??= Stopwatch.GetElapsedTime(stageStart);
            stages[(int)value.Stage] = value.Status;
            if (value.Status is BootstrapStatus.Failed or BootstrapStatus.Cancelled ||
                value.Stage == BootstrapStage.Deploy && value.Status == BootstrapStatus.Completed) Stop();
            if (!Render()) WriteLine($"{StageName(value.Stage)}: {StatusText(value.Stage)} - {value.Message}");
            lastOutput = Stopwatch.GetTimestamp();
        }
    }

    internal void ReportDownload(RuntimeDownloadProgress value)
    {
        lock (gate)
        {
            if (stopped || disposed) return;
            bool phaseChanged = download?.Phase != value.Phase || download?.ApplicationName != value.ApplicationName;
            download = value;
            activity = null;
            if (!phaseChanged && Stopwatch.GetElapsedTime(lastOutput) < TimeSpan.FromSeconds(1)) return;
            if (!Render()) WriteLine(DownloadText(value));
            lastOutput = Stopwatch.GetTimestamp();
        }
    }

    internal void ReportActivity(string message)
    {
        lock (gate)
        {
            if (stopped || disposed) return;
            download = null;
            activity = message;
            if (!Render()) WriteLine(message);
            lastOutput = Stopwatch.GetTimestamp();
        }
    }

    internal void ReportWarning(string message)
    {
        lock (gate)
        {
            if (!stopped && !disposed) AddWarning(current?.Stage ?? BootstrapStage.Environment, message);
        }
    }

    /// <summary>Retains warnings and distinguishes confirmed readiness from a legacy process-only handoff.</summary>
    internal void Complete(BootstrapResult value, string sessionId, string? logPath)
    {
        lock (gate)
        {
            if (disposed || result is not null) return;
            result = value;
            Stop();
            if (value.Outcome == BootstrapOutcome.Failed || warnings.Count > 0)
            {
                diagnosticSession = sessionId;
                diagnosticLog = logPath;
            }
            if (!Render())
            {
                WriteLine(Subtitle());
                WriteLine(FinalMessage());
                WriteLine($"Finished in {ElapsedText()}");
                WriteDiagnostics();
            }
        }
    }

    internal void ShowDiagnostics(string sessionId, string? logPath)
    {
        lock (gate)
        {
            if (disposed) return;
            diagnosticSession = sessionId;
            diagnosticLog = logPath;
            if (!Render()) WriteDiagnostics();
        }
    }

    private void AddWarning(BootstrapStage stage, string message)
    {
        if (warnings.Contains((stage, message))) return;
        warnings.Add((stage, message));
        if (!Render()) WriteLine($"Warning - {StageName(stage)}: {message}");
        lastOutput = Stopwatch.GetTimestamp();
    }

    private void Refresh()
    {
        lock (gate)
        {
            if (stopped || disposed || current is null) return;
            if (interactive) Render();
            else if (Stopwatch.GetElapsedTime(lastOutput) >= TimeSpan.FromSeconds(15))
            {
                WriteLine($"{activity ?? current.Message} - elapsed {ElapsedText()}");
                lastOutput = Stopwatch.GetTimestamp();
            }
        }
    }

    private void Stop()
    {
        stopped = true;
        elapsed ??= Stopwatch.GetElapsedTime(started);
        download = null;
    }

    private string ElapsedText()
    {
        TimeSpan duration = elapsed ?? Stopwatch.GetElapsedTime(started);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private string StageElapsedText(BootstrapStage stage)
    {
        int index = (int)stage;
        if (stageStarts[index] is not long timestamp) return "";
        TimeSpan duration = stageDurations[index] ?? Stopwatch.GetElapsedTime(timestamp);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private string Subtitle() => result?.Outcome switch
    {
        BootstrapOutcome.Succeeded when result.ReadinessConfirmed => "Deployment environment ready",
        BootstrapOutcome.Succeeded => "Deployment application launched",
        BootstrapOutcome.Cancelled => "Startup cancelled",
        BootstrapOutcome.Failed => "Startup failed",
        _ => "Preparing your deployment environment"
    };

    private string FinalMessage() => result?.Outcome switch
    {
        BootstrapOutcome.Succeeded when result.ReadinessConfirmed => "Continue in Foundry Deploy.",
        BootstrapOutcome.Succeeded => warnings.Count > 0 ? "Foundry Deploy was launched." : "Foundry Deploy was launched. Readiness is unverified.",
        _ => current?.Message ?? "Preparing your deployment environment"
    };

    private string StatusText(BootstrapStage stage) => stages[(int)stage] switch
    {
        BootstrapStatus.Running => download is not null && current?.Stage == stage ? ProgressName(download.Phase) : "In progress",
        BootstrapStatus.Completed when stage == BootstrapStage.Deploy && result is not null =>
            result.ReadinessConfirmed ? "Ready" : "Unverified",
        BootstrapStatus.Completed => warnings.Any(warning => warning.Stage == stage) ? "Done (warning)" : "Done",
        BootstrapStatus.Failed => "Failed",
        BootstrapStatus.Cancelled => "Cancelled",
        _ => stopped ? "Not started" : "Waiting"
    };

    private ConsoleColor StageColor(BootstrapStage stage) => stages[(int)stage] switch
    {
        BootstrapStatus.Failed => ConsoleColor.Red,
        BootstrapStatus.Cancelled => ConsoleColor.Yellow,
        _ when warnings.Any(warning => warning.Stage == stage) => ConsoleColor.Yellow,
        BootstrapStatus.Completed => ConsoleColor.Green,
        BootstrapStatus.Running => ConsoleColor.Cyan,
        _ => ConsoleColor.DarkGray
    };

    private static string StageName(BootstrapStage stage) => stage switch
    {
        BootstrapStage.Environment => "Environment",
        BootstrapStage.Connect => "Network connection",
        BootstrapStage.System => "Clock and time zone",
        BootstrapStage.DeploymentPreparation => "Deployment files",
        BootstrapStage.Deploy => "Deployment application",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static string ProgressName(RuntimeProgressPhase phase) => phase switch
    {
        RuntimeProgressPhase.Verification => "Verifying",
        RuntimeProgressPhase.Extraction => "Extracting",
        _ => "Downloading"
    };
    private static string DownloadText(RuntimeDownloadProgress value)
    {
        string total = value.TotalBytes is > 0
            ? $" / {value.TotalBytes.Value / 1048576d:F1} MB - {Math.Min(100, 100d * value.BytesReceived / value.TotalBytes.Value):F0}%"
            : " MB";
        return $"{ProgressName(value.Phase)} {value.ApplicationName}: {value.BytesReceived / 1048576d:F1}{total}";
    }

    private static string DownloadProgressText(RuntimeDownloadProgress value)
    {
        double received = Math.Max(0, value.BytesReceived) / 1048576d;
        if (value.TotalBytes is not > 0) return $"{received:F1} MB transferred";
        double fraction = Math.Clamp((double)value.BytesReceived / value.TotalBytes.Value, 0, 1);
        int filled = (int)(fraction * 40);
        return $"{received:F1} MB / {value.TotalBytes.Value / 1048576d:F1} MB  [{new string('=', filled)}{new string(' ', 40 - filled)}] {fraction:P0}";
    }

    private bool Render()
    {
        if (!interactive) return false;
        try
        {
            int width = Math.Min(System.Console.WindowWidth, System.Console.BufferWidth) - 1;
            if (width < 44) throw new IOException("Console is too narrow for the progress screen.");
            var lines = new List<(string Text, ConsoleColor Color)>();
            void Add(string text, ConsoleColor color = ConsoleColor.Gray)
            {
                text = OneLine(text);
                while (text.Length > width)
                {
                    int split = text.LastIndexOf(' ', width, width);
                    if (split <= 0) split = width;
                    lines.Add((text[..split], color));
                    text = text[split..].TrimStart();
                }
                lines.Add((text, color));
            }
            Add("Foundry Bootstrap", ConsoleColor.White);
            Add(Subtitle());
            Add("");
            foreach (BootstrapStage stage in Enum.GetValues<BootstrapStage>())
                Add($"  {StageName(stage),-27}{StatusText(stage),-15}{(width >= 50 ? StageElapsedText(stage) : "")}");
            Add("");
            Add(stopped ? FinalMessage() : download is not null ? $"{ProgressName(download.Phase)} {download.ApplicationName}" : activity is not null ? $"[{"|/-\\"[(int)(Stopwatch.GetElapsedTime(started).TotalSeconds % 4)]}] {activity}" : current?.Message ?? "Starting...");
            if (download is not null) Add(DownloadProgressText(download));
            Add("");
            Add($"{(stopped ? "Finished in" : "Elapsed:")} {ElapsedText()}");
            foreach (var warning in warnings.Take(3)) Add($"Warning: {warning.Message}", ConsoleColor.Yellow);
            if (warnings.Count > 3) Add($"{warnings.Count - 3} more warnings are available in the log.", ConsoleColor.Yellow);
            if (diagnosticSession is not null)
            {
                Add("");
                Add($"Session: {diagnosticSession}");
                Add(diagnosticLog is null ? "A diagnostic log file could not be opened." : $"Log: {diagnosticLog}");
            }
            int rows = Math.Max(paintedRows, lines.Count);
            if (rows >= Math.Min(System.Console.WindowHeight, System.Console.BufferHeight))
                throw new IOException("Console is too short for the progress screen.");
            for (int row = 0; row < rows; row++)
            {
                System.Console.SetCursorPosition(0, row);
                var line = row < lines.Count ? lines[row] : (string.Empty, ConsoleColor.Gray);
                System.Console.ForegroundColor = line.Item2;
                output.Write(line.Item1.PadRight(width));
                if (row is >= 3 and < 8)
                {
                    BootstrapStage stage = (BootstrapStage)(row - 3);
                    System.Console.SetCursorPosition(2, row);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    output.Write(StageName(stage));
                    System.Console.SetCursorPosition(29, row);
                    System.Console.ForegroundColor = StageColor(stage);
                    output.Write(StatusText(stage));
                    if (width >= 50)
                    {
                        System.Console.SetCursorPosition(44, row);
                        System.Console.ForegroundColor = ConsoleColor.DarkGray;
                        output.Write(StageElapsedText(stage));
                    }
                }
            }
            System.Console.SetCursorPosition(0, rows);
            System.Console.ForegroundColor = originalColor;
            paintedRows = rows;
            return true;
        }
        catch (Exception exception) when (IsConsoleFailure(exception))
        {
            interactive = false;
            RestoreConsole();
            WriteLine("");
            foreach (var warning in warnings) WriteLine($"Warning: {warning.Message}");
            return false;
        }
    }

    private void WriteDiagnostics()
    {
        if (diagnosticSession is null) return;
        WriteLine($"Session: {diagnosticSession}");
        WriteLine(diagnosticLog is null ? "A diagnostic log file could not be opened." : $"Log: {diagnosticLog}");
    }

    private void WriteLine(string message)
    {
        try { output.WriteLine(OneLine(message)); }
        catch (Exception exception) when (IsConsoleFailure(exception)) { }
    }

    private static string OneLine(string value) => string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static bool IsConsoleFailure(Exception exception) => exception is IOException or InvalidOperationException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;

    private void RestoreConsole()
    {
        try
        {
            if (originalCursorVisible is not null)
            {
                System.Console.ForegroundColor = originalColor;
                System.Console.CursorVisible = originalCursorVisible.Value;
            }
        }
        catch (Exception exception) when (IsConsoleFailure(exception)) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            heartbeat.Dispose();
            RestoreConsole();
        }
    }
}
