// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Console;

/// <summary>Shows best-effort setup progress, falling back to plain output when console rendering is unavailable.</summary>
internal sealed class PostInstallConsole : IProgress<PostInstallProgress>, IDisposable
{
    private readonly object gate = new();
    private readonly TextWriter output;
    private readonly string logPath;
    private readonly Timer heartbeat;
    private readonly Dictionary<string, (string Status, int? ExitCode)> reportedActions = [];
    private PostInstallProgress current;
    private OrchestrationOutcome? result;
    private long snapshotAt = Stopwatch.GetTimestamp();
    private long lastOutputAt = Stopwatch.GetTimestamp();
    private bool interactive;
    private bool disposed;
    private bool resumeReported;
    private bool handoffStarted;
    private bool? originalCursorVisible;
    private ConsoleColor originalColor;
    private int paintedRows;
    private int? setupSecondsRemaining;
    private string? reportedActivity;
    private string? reportedHeading;
    private string? reportedDomain;

    internal PostInstallConsole(PreOobeExecutionPlan plan, string logPath, TextWriter? output = null)
    {
        this.output = output ?? System.Console.Out;
        this.logPath = logPath;
        current = new(plan.Actions.Select(action => new PostInstallActionProgress(action.Id,
            PostInstallProgress.GetActionName(action), "Waiting")).ToArray(), "Verifying");
        if (output is null)
        {
            try
            {
                if (!System.Console.IsOutputRedirected)
                {
                    System.Console.Title = "Foundry Post-installation";
                    originalColor = System.Console.ForegroundColor;
                    originalCursorVisible = System.Console.CursorVisible;
                    System.Console.Clear();
                    System.Console.CursorVisible = false;
                    interactive = true;
                }
            }
            catch (Exception error) when (IsConsoleFailure(error)) { RestoreConsole(); }
        }
        if (!Render())
        {
            WriteLine("Foundry Post-installation");
            WriteLine(Activity());
        }
        heartbeat = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Report(PostInstallProgress value)
    {
        lock (gate)
        {
            if (disposed || result is not null) return;
            current = value;
            snapshotAt = Stopwatch.GetTimestamp();
            if (!Render()) WriteChanges();
            lastOutputAt = Stopwatch.GetTimestamp();
        }
    }

    internal void Complete(OrchestrationOutcome value)
    {
        lock (gate)
        {
            if (disposed || result is not null) return;
            result = value;
            current = current with { Status = value.Status, RestartSecondsRemaining = null };
            if (!Render())
            {
                WriteChanges();
                WriteLine(Summary());
                WriteLine($"Log: {logPath}");
            }
        }
    }

    /// <summary>Keeps terminal results visible before returning success to Setup; restart and failure exits are not delayed.</summary>
    internal async Task WaitForSetupAsync(Func<TimeSpan, Task>? delay = null)
    {
        lock (gate)
        {
            if (disposed || handoffStarted || result is not { ExitCode: 0 }) return;
            handoffStarted = true;
        }
        delay ??= duration => Task.Delay(duration);
        for (int seconds = 10; seconds >= 0; seconds--)
        {
            lock (gate)
            {
                if (disposed) return;
                setupSecondsRemaining = seconds;
                if (!Render()) WriteLine(HandoffText());
            }
            if (seconds > 0) await delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    private void Refresh()
    {
        lock (gate)
        {
            if (disposed || result is not null) return;
            if (Render() || Stopwatch.GetElapsedTime(lastOutputAt) < TimeSpan.FromSeconds(15)) return;
            PostInstallActionProgress? running = current.Actions.FirstOrDefault(action => action.Status == "Running");
            WriteLine(running is null ? Activity() : ActionText(running));
            lastOutputAt = Stopwatch.GetTimestamp();
        }
    }

    private void WriteChanges()
    {
        if (current.IsResuming && !resumeReported)
        {
            WriteLine("Resuming after restart");
            resumeReported = true;
        }
        string heading = ActionHeading();
        if (reportedHeading != heading) WriteLine(heading);
        reportedHeading = heading;
        for (int index = 0; index < current.Actions.Count; index++)
        {
            PostInstallActionProgress action = current.Actions[index];
            var state = (action.Status, action.ExitCode);
            if (reportedActions.TryGetValue(action.Id, out var previous) && state == previous) continue;
            reportedActions[action.Id] = state;
            if (action.Status != "Waiting") WriteLine(ActionText(action));
        }
        string activity = Activity();
        if (reportedActivity != activity) WriteLine(activity);
        reportedActivity = activity;
        string domain = DomainText();
        if (reportedDomain != domain && domain.Length > 0) WriteLine(domain);
        reportedDomain = domain;
    }

    private string Activity() => current.RestartSecondsRemaining switch
    {
        > 0 => $"Restarting in {current.RestartSecondsRemaining} seconds...",
        0 => "Restarting now...",
        _ => current.Status switch
        {
            "Verifying" => "Verifying post-installation content...",
            "Succeeded" => "Post-installation completed.",
            "CompletedWithErrors" => "Post-installation completed with warnings. Review the execution result and logs.",
            "AwaitingRestart" => "Waiting for a Windows Setup restart.",
            "Failed" or "Interrupted" or "Unavailable" => "Post-installation stopped. Review the execution result and logs before continuing Windows Setup.",
            "Completing" => "Finishing cleanup...",
            _ => "Running post-installation actions..."
        }
    };

    private string HandoffText() => setupSecondsRemaining > 0
        ? $"Continuing Windows Setup in {setupSecondsRemaining} seconds..." : "Continuing Windows Setup...";

    private string Summary() => $"Succeeded: {current.Actions.Count(action => action.Status == "Succeeded")}  " +
        $"Failed: {current.Actions.Count(action => action.Status is "Failed" or "Interrupted")}  " +
        $"Skipped: {current.Actions.Count(action => action.Status == "Skipped")}  Warnings: {current.WarningCount}";

    private string DomainText() => current.DomainResult is not { } domain ? string.Empty :
        $"Domain - Join: {domain.Join.State}; Placement: {domain.Placement.State}; Membership: {domain.Membership.State}; " +
        $"Restart: {domain.Restart}; Cleanup: {domain.Cleanup}" +
        (DomainWarning(domain) is { } warning ? ". " + warning : string.Empty);

    /// <summary>Explains a domain outcome the technician must follow up, or null when the phase states say enough.</summary>
    private static string? DomainWarning(DomainJoinResult domain) => domain.Join.State switch
    {
        DomainJoinPhaseState.Unknown => "Domain join outcome unknown; membership is checked after restart",
        DomainJoinPhaseState.Succeeded => domain.Placement.State switch
        {
            DomainJoinPhaseState.Failed when domain.Placement.FailureCode == DomainJoinFailureCode.OrganizationalUnitNotFound =>
                "Domain joined in the default location; target OU not found",
            DomainJoinPhaseState.Failed => "Domain joined; target OU placement failed",
            DomainJoinPhaseState.Unverified or DomainJoinPhaseState.Unknown => "Domain joined; target OU placement not confirmed",
            _ => null
        },
        _ => null
    };

    private int ActiveActionIndex()
    {
        int running = current.Actions.ToList().FindIndex(action => action.Status == "Running");
        if (running >= 0) return running;
        if (current.Status == "AwaitingRestart")
            return Math.Max(0, current.Actions.ToList().FindLastIndex(action => action.Status != "Waiting"));
        int waiting = current.Actions.ToList().FindIndex(action => action.Status == "Waiting");
        return waiting >= 0 ? waiting : Math.Max(0, current.Actions.Count - 1);
    }

    private string ActionHeading()
    {
        int total = current.Actions.Count;
        if (total == 0) return "No actions";
        if (current.Status is "Succeeded" or "CompletedWithErrors" or "Failed" or "Interrupted" or "Unavailable")
        {
            int completed = current.Actions.Count(action => action.Status is "Succeeded" or "Failed" or "Skipped" or "Interrupted");
            return $"Actions completed: {completed} of {total}";
        }
        return current.Status == "Verifying" ? $"Actions ({total})" : $"Action {ActiveActionIndex() + 1} of {total}";
    }

    private string ActionText(PostInstallActionProgress action, int width = 120)
    {
        int statusWidth = Math.Max(13, current.Actions.Max(value => value.Status.Length) + 2);
        int durationWidth = Math.Max(5, current.Actions.Max(value => ElapsedText(value).Length));
        int exitWidth = current.Actions.Max(value => ExitText(value).Length);
        int nameWidth = Math.Min(current.Actions.Max(value => OneLine(value.Name).Length),
            Math.Max(1, width - statusWidth - durationWidth - exitWidth - 7));
        string status = $"[{action.Status}]".PadRight(statusWidth);
        string name = Shorten(OneLine(action.Name), nameWidth).PadRight(nameWidth);
        string elapsed = ElapsedText(action);
        return $"- {status} {name}  {elapsed.PadRight(durationWidth)}{ExitText(action)}".TrimEnd();
    }

    private static string ExitText(PostInstallActionProgress action) => action.Status is "Failed" or "Interrupted" && action.ExitCode is int code
        ? $"  (exit {code})" : string.Empty;

    private string ElapsedText(PostInstallActionProgress action)
    {
        if (action.Elapsed is null && action.Status != "Running") return string.Empty;
        TimeSpan duration = action.Elapsed ?? TimeSpan.Zero;
        if (action.Status == "Running" && result is null) duration += Stopwatch.GetElapsedTime(snapshotAt);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private bool Render()
    {
        if (!interactive) return false;
        try
        {
            int width = Math.Min(System.Console.WindowWidth, System.Console.BufferWidth) - 1;
            int height = Math.Min(System.Console.WindowHeight, System.Console.BufferHeight) - 1;
            if (width < 60 || height < 16) throw new IOException("Console is too small for the progress screen.");
            var lines = new List<(string Text, ConsoleColor Color)>();
            void Add(string text, ConsoleColor color = ConsoleColor.Gray) => lines.Add((OneLine(text), color));
            Add("Foundry Post-installation", ConsoleColor.White);
            Add(current.IsResuming ? "Resuming after restart" : "Preparing Windows before OOBE");
            Add("");
            int capacity = Math.Max(1, height - 16);
            int active = ActiveActionIndex();
            int first = Math.Clamp(active - capacity / 2, 0, Math.Max(0, current.Actions.Count - capacity));
            int last = Math.Min(first + capacity, current.Actions.Count);
            Add(ActionHeading());
            Add("");
            for (int index = first; index < last; index++)
            {
                PostInstallActionProgress action = current.Actions[index];
                Add(ActionText(action, width), StatusColor(action.Status));
            }
            Add("");
            Add("");
            Add(Activity(), StatusColor(current.Status));
            if (current.DomainResult is { } domain)
            {
                Add($"Domain - Join: {domain.Join.State}; Placement: {domain.Placement.State}; Membership: {domain.Membership.State}");
                Add($"Restart: {domain.Restart}; Cleanup: {domain.Cleanup}");
                if (DomainWarning(domain) is { } warning) Add(warning, ConsoleColor.Yellow);
            }
            if (result is not null) Add(Summary());
            if (setupSecondsRemaining is not null)
            {
                Add("");
                Add(HandoffText(), ConsoleColor.Yellow);
            }
            Add("");
            Add($"Log: {logPath}");
            int rows = Math.Min(height, Math.Max(paintedRows, lines.Count));
            for (int row = 0; row < rows; row++)
            {
                System.Console.SetCursorPosition(0, row);
                var line = row < lines.Count ? lines[row] : (string.Empty, ConsoleColor.Gray);
                System.Console.ForegroundColor = line.Item2;
                output.Write(Shorten(line.Item1, width).PadRight(width));
            }
            System.Console.SetCursorPosition(0, rows);
            System.Console.ForegroundColor = originalColor;
            paintedRows = rows;
            return true;
        }
        catch (Exception error) when (IsConsoleFailure(error))
        {
            interactive = false;
            RestoreConsole();
            WriteLine("");
            WriteLine("Foundry Post-installation");
            return false;
        }
    }

    private static ConsoleColor StatusColor(string status) => status switch
    {
        "Running" or "Verifying" or "Completing" => ConsoleColor.Cyan,
        "Succeeded" => ConsoleColor.Green,
        "CompletedWithErrors" or "AwaitingRestart" => ConsoleColor.Yellow,
        "Failed" or "Interrupted" or "Unavailable" => ConsoleColor.Red,
        _ => ConsoleColor.Gray
    };

    private static string Shorten(string value, int width) => value.Length <= width ? value
        : width <= 3 ? value[..width] : value[..(width - 3)] + "...";

    private static string OneLine(string value) => string.Concat(value.Select(character =>
        char.IsControl(character) || char.GetUnicodeCategory(character) is UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : character));

    private void WriteLine(string message)
    {
        try { output.WriteLine(OneLine(message)); }
        catch (Exception error) when (IsConsoleFailure(error)) { }
    }

    private static bool IsConsoleFailure(Exception error) => error is IOException or InvalidOperationException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;

    private void RestoreConsole()
    {
        try
        {
            if (originalCursorVisible is null) return;
            System.Console.ForegroundColor = originalColor;
            System.Console.CursorVisible = originalCursorVisible.Value;
        }
        catch (Exception error) when (IsConsoleFailure(error)) { }
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
