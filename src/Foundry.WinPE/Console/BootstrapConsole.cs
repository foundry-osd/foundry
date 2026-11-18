// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.WinPE.Console;

/// <summary>
/// Maintains a compact WinPE progress screen, with plain output when cursor control is unavailable. It shows two lines: the
/// guardian's environment stage and the UI process ("Foundry"), on which every UI phase is reported.
/// </summary>
internal sealed class BootstrapConsole : IDisposable
{
    private readonly object gate = new();
    private readonly TextWriter output;
    private readonly Timer heartbeat;
    private readonly long started = Stopwatch.GetTimestamp();
    private const int EnvironmentRow = 0;
    private const int FoundryRow = 1;
    private const int RowCount = 2;
    private readonly BootstrapStatus[] stages = new BootstrapStatus[RowCount];
    private readonly long?[] stageStarts = new long?[RowCount];
    private readonly TimeSpan?[] stageDurations = new TimeSpan?[RowCount];
    private readonly List<(BootstrapStage Stage, string Message)> warnings = [];
    private BootstrapProgress? current;
    private BootstrapResult? result;
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
        if (!Render()) WriteLine("Foundry WinPE");
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
            int row = Row(value.Stage);
            if (value.Status == BootstrapStatus.Running) stageStarts[row] ??= Stopwatch.GetTimestamp();
            else if (stageStarts[row] is long stageStart)
                stageDurations[row] ??= Stopwatch.GetElapsedTime(stageStart);
            stages[row] = value.Status;
            if (value.Status is BootstrapStatus.Failed or BootstrapStatus.Cancelled ||
                row == FoundryRow && value.Status == BootstrapStatus.Completed) Stop();
            if (!Render()) WriteLine($"{RowName(row)}: {StatusText(row)} - {value.Message}");
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

    /// <summary>Shows the final outcome and keeps the warnings; later reports cannot replace it.</summary>
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
        if (!Render()) WriteLine($"Warning - {RowName(Row(stage))}: {message}");
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
                WriteLine($"{current.Message} - elapsed {ElapsedText()}");
                lastOutput = Stopwatch.GetTimestamp();
            }
        }
    }

    private void Stop()
    {
        stopped = true;
        elapsed ??= Stopwatch.GetElapsedTime(started);
    }

    private string ElapsedText()
    {
        TimeSpan duration = elapsed ?? Stopwatch.GetElapsedTime(started);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private string StageElapsedText(int row)
    {
        if (stageStarts[row] is not long timestamp) return "";
        TimeSpan duration = stageDurations[row] ?? Stopwatch.GetElapsedTime(timestamp);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private string Subtitle() => result?.Outcome switch
    {
        BootstrapOutcome.Succeeded => "Foundry finished",
        BootstrapOutcome.Cancelled => "Startup cancelled",
        BootstrapOutcome.Failed => "Startup failed",
        _ => "Preparing your deployment environment"
    };

    private string FinalMessage() => current?.Message ?? "Preparing your deployment environment";

    private string StatusText(int row) => stages[row] switch
    {
        BootstrapStatus.Running => "In progress",
        BootstrapStatus.Completed => warnings.Any(warning => Row(warning.Stage) == row) ? "Done (warning)" : "Done",
        BootstrapStatus.Failed => "Failed",
        BootstrapStatus.Cancelled => "Cancelled",
        _ => stopped ? "Not started" : "Waiting"
    };

    private ConsoleColor StageColor(int row) => stages[row] switch
    {
        BootstrapStatus.Failed => ConsoleColor.Red,
        BootstrapStatus.Cancelled => ConsoleColor.Yellow,
        _ when warnings.Any(warning => Row(warning.Stage) == row) => ConsoleColor.Yellow,
        BootstrapStatus.Completed => ConsoleColor.Green,
        BootstrapStatus.Running => ConsoleColor.Cyan,
        _ => ConsoleColor.DarkGray
    };

    // The UI process runs Connect, system preparation and Deploy: all its phases share the "Foundry" line.
    private static int Row(BootstrapStage stage) => stage == BootstrapStage.Environment ? EnvironmentRow : FoundryRow;

    private static string RowName(int row) => row == EnvironmentRow ? "Environment" : "Foundry";

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
            Add("Foundry WinPE", ConsoleColor.White);
            Add(Subtitle());
            Add("");
            for (int row = 0; row < RowCount; row++)
                Add($"  {RowName(row),-27}{StatusText(row),-15}{(width >= 50 ? StageElapsedText(row) : "")}");
            Add("");
            Add(stopped ? FinalMessage() : current?.Message ?? "Starting...");
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
                if (row >= 3 && row < 3 + RowCount)
                {
                    int stageRow = row - 3;
                    System.Console.SetCursorPosition(2, row);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    output.Write(RowName(stageRow));
                    System.Console.SetCursorPosition(29, row);
                    System.Console.ForegroundColor = StageColor(stageRow);
                    output.Write(StatusText(stageRow));
                    if (width >= 50)
                    {
                        System.Console.SetCursorPosition(44, row);
                        System.Console.ForegroundColor = ConsoleColor.DarkGray;
                        output.Write(StageElapsedText(stageRow));
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
