// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Serilog;
using Serilog.Events;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class UiProcessExitTests
{
    [Fact]
    public void ApplicationExit_IsTheProcessExitCodeWithoutAnyReadinessReport()
    {
        var fixture = new Fixture();

        Assert.Equal(1, UiProcessExit.Run(() => () => 1, fixture, fixture.Logger));

        Assert.Empty(fixture.Readiness);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public void SetupFailure_IsAStartupFailureReportedAsSuch()
    {
        var fixture = new Fixture();

        int exitCode = UiProcessExit.Run(() => throw new InvalidOperationException("setup"), fixture, fixture.Logger);

        Assert.Equal(21, exitCode);
        Assert.Equal(["startup_failed:21"], fixture.Readiness);
        Assert.Equal(LogEventLevel.Fatal, Assert.Single(fixture.Events).Level);
    }

    [Fact]
    public void ExceptionLeavingTheRunningApplication_IsACrashWithoutStartupFailure()
    {
        var fixture = new Fixture();

        int exitCode = UiProcessExit.Run(() => () => throw new InvalidOperationException("teardown"), fixture, fixture.Logger);

        Assert.Equal(UiProcessExit.UnhandledExceptionExitCode, exitCode);
        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain(exitCode, new[] { 20, 21, 22, 23 });
        Assert.Empty(fixture.Readiness);
        LogEvent crash = Assert.Single(fixture.Events);
        Assert.Equal(LogEventLevel.Fatal, crash.Level);
        Assert.IsType<InvalidOperationException>(crash.Exception);
    }

    private sealed class Fixture : IUiReadinessReporter
    {
        private readonly List<LogEvent> events = [];

        internal Fixture() =>
            Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegatingSink(events.Add)).CreateLogger();

        internal ILogger Logger { get; }
        internal IReadOnlyList<LogEvent> Events => events;
        internal List<string> Readiness { get; } = [];

        public void ReportConfigurationLoaded() => Readiness.Add("configuration_loaded");

        public void ReportUiReady() => Readiness.Add("ui_ready");

        public void ReportStartupFailed(int exitCode) => Readiness.Add($"startup_failed:{exitCode}");
    }
}
