// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Startup;

namespace Foundry.AutopilotInteractive.Tests.Startup;

public sealed class AutopilotInteractiveCommandLineTests
{
    [Fact]
    public void Parse_ReadsConfigAndDeadline()
    {
        List<string> warnings = [];

        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse(
            ["--config", @"C:\x.json", "--deadline", "2026-11-01T10:00:00Z"],
            warnings.Add);

        Assert.Equal(@"C:\x.json", commandLine.ConfigPath);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), commandLine.DeadlineUtc);
        Assert.Equal(TimeSpan.Zero, commandLine.DeadlineUtc!.Value.Offset);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Parse_NormalizesDeadlineWithOffsetToUtc()
    {
        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse(
            ["--deadline", "2026-11-01T12:00:00+02:00"],
            _ => { });

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), commandLine.DeadlineUtc);
        Assert.Equal(TimeSpan.Zero, commandLine.DeadlineUtc!.Value.Offset);
    }

    [Fact]
    public void Parse_IgnoresMalformedDeadline_WithWarning()
    {
        List<string> warnings = [];

        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse(
            ["--config", @"C:\x.json", "--deadline", "tomorrow"],
            warnings.Add);

        Assert.Null(commandLine.DeadlineUtc);
        Assert.Equal(@"C:\x.json", commandLine.ConfigPath);
        Assert.Single(warnings);
    }

    [Fact]
    public void Parse_ReturnsNullConfig_WhenMissing()
    {
        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse([], _ => { });

        Assert.Null(commandLine.ConfigPath);
        Assert.Null(commandLine.DeadlineUtc);
    }

    [Fact]
    public void Parse_IgnoresUnknownArgumentsAndMissingValues_WithWarnings()
    {
        List<string> warnings = [];

        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse(
            ["--unknown", "--config"],
            warnings.Add);

        Assert.Null(commandLine.ConfigPath);
        Assert.Equal(2, warnings.Count);
    }
}
