// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy;
using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Foundry.Wpf.Shell;
using Serilog;
using Serilog.Events;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class WinPeUiSessionTests
{
    [Fact]
    public async Task ConnectSuccess_PreparesTheSystemOnceBetweenThePhasesAndHandsOverToDeploy()
    {
        var fixture = new Fixture { DeployExitCode = DeployPhase.UnhandledExceptionExitCode };

        int exitCode = await fixture.RunAsync();

        Assert.Equal(DeployPhase.UnhandledExceptionExitCode, exitCode);
        Assert.Equal(["connect", "system", "deploy"], fixture.Calls);
        Assert.Equal([UiPhase.Connect, UiPhase.System, UiPhase.Deploy], fixture.States.Select(state => state.Phase));
        Assert.All(fixture.States, state => Assert.False(state.DestructiveStarted));
        DeployPhaseOptions options = fixture.DeployOptions!;
        Assert.Same(fixture.Key, options.InitialDeploymentKey);
        Assert.Equal("Dark", options.ThemeMode);
        Assert.Equal("fr-FR", options.CultureName);
    }

    [Fact]
    public async Task ConnectWindow_IsClosedOnlyOnceDeployRendered()
    {
        var fixture = new Fixture();
        fixture.Deploy = options =>
        {
            Assert.Equal(0, fixture.Window.CloseCount);
            options.OnFirstContentRendered();
            Assert.Equal(1, fixture.Window.CloseCount);
            options.OnFirstContentRendered();
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());
        Assert.Equal(1, fixture.Window.CloseCount);
    }

    [Fact]
    public async Task ConnectWindow_IsClosedWhenDeployEndsWithoutRendering()
    {
        var fixture = new Fixture { DeployExitCode = DeployPhase.UnhandledExceptionExitCode };

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Window.CloseCount);
    }

    [Fact]
    public async Task ConnectSuccessWithoutWindow_StillRunsDeploy()
    {
        var fixture = new Fixture { HasWindow = false };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());
        Assert.Contains("deploy", fixture.Calls);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public async Task ConnectWithoutSuccess_EndsTheSessionWithItsCodeAndDisposesTheKey(int connectExitCode)
    {
        var fixture = new Fixture { ConnectExitCode = connectExitCode };

        int exitCode = await fixture.RunAsync();

        Assert.Equal(connectExitCode, exitCode);
        Assert.Equal(["connect"], fixture.Calls);
        Assert.Equal([UiPhase.Connect], fixture.States.Select(state => state.Phase));
        Assert.False(fixture.Key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    public async Task ConnectEndingBeforeItsFirstWindow_ReportsStartupFailureOnlyForFailures(int connectExitCode, bool failed)
    {
        var fixture = new Fixture { ConnectExitCode = connectExitCode, ConnectRenders = false };

        await fixture.RunAsync();

        Assert.Equal(failed ? ["configuration_loaded", $"startup_failed:{connectExitCode}"] : ["configuration_loaded"],
            fixture.Readiness);
    }

    [Fact]
    public async Task ConnectFailureAfterItsFirstWindow_NeverReportsStartupFailure()
    {
        var fixture = new Fixture { ConnectExitCode = 23 };

        await fixture.RunAsync();

        Assert.Equal(["configuration_loaded", "ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task Readiness_IsReportedOnceAtConnectsFirstRenderAndNeverByDeploy()
    {
        var fixture = new Fixture();
        fixture.Deploy = options =>
        {
            options.OnFirstContentRendered();
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        await fixture.RunAsync();

        Assert.Equal(["configuration_loaded", "ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task SystemPreparationFailure_IsAWarningAndDeployStillStarts()
    {
        var fixture = new Fixture { SystemPreparationFails = true };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Equal(["connect", "system", "deploy"], fixture.Calls);
        Assert.Contains(fixture.Events, item => item.Level == LogEventLevel.Warning &&
            item.MessageTemplate.Text.Contains("System preparation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutsideWindowsPe_SystemPreparationIsSkippedWithAnInformation()
    {
        var fixture = new Fixture { IsWinPe = false };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Equal(["connect", "deploy"], fixture.Calls);
        Assert.Equal([UiPhase.Connect, UiPhase.System, UiPhase.Deploy], fixture.States.Select(state => state.Phase));
        Assert.Contains(fixture.Events, item => item.Level == LogEventLevel.Information &&
            item.MessageTemplate.Text.Contains("skipped outside Windows PE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DestructiveStep_IsRecordedInTheStateFileBeforeTheStepContinues()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryUiState-").FullName;
        try
        {
            string path = Path.Combine(directory, "ui-state.json");
            UiState? recorded = null;
            var fixture = new Fixture { StateWriter = new UiStateFileWriter(path, new LoggerConfiguration().CreateLogger()) };
            fixture.Deploy = options =>
            {
                Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: false), UiStateFile.Read(path));
                options.OnDestructiveStepStarted();
                recorded = UiStateFile.Read(path);
                return Task.FromResult(DeployPhase.SuccessExitCode);
            };

            await fixture.RunAsync();

            Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true), recorded);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UnwritableStatePath_NeverFailsTheSession()
    {
        string directory = Directory.CreateTempSubdirectory("FoundryUiState-").FullName;
        try
        {
            // A file where the state file's directory must be: every write fails.
            string blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(blocker, string.Empty);
            var fixture = new Fixture
            {
                StateWriter = new UiStateFileWriter(Path.Combine(blocker, "ui-state.json"), new LoggerConfiguration().CreateLogger())
            };
            fixture.Deploy = options =>
            {
                options.OnDestructiveStepStarted();
                return Task.FromResult(DeployPhase.UnhandledExceptionExitCode);
            };

            Assert.Equal(DeployPhase.UnhandledExceptionExitCode, await fixture.RunAsync());
            Assert.Equal(["connect", "system", "deploy"], fixture.Calls);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SkipConnect_GoesStraightToDeployWithoutKeyPreferencesOrSystemPreparation()
    {
        var fixture = new Fixture { SkipConnect = true };
        fixture.Deploy = options =>
        {
            options.OnFirstContentRendered();
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Equal(["deploy"], fixture.Calls);
        Assert.Equal([UiPhase.Deploy], fixture.States.Select(state => state.Phase));
        DeployPhaseOptions options = fixture.DeployOptions!;
        Assert.Null(options.InitialDeploymentKey);
        Assert.Null(options.ThemeMode);
        Assert.Null(options.CultureName);
        // Without Connect, Deploy's window is the session's first window.
        Assert.Equal(["ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task ConnectFailureWithAWindow_ClosesItExactlyOnce()
    {
        var fixture = new Fixture { ConnectExitCode = 23, WindowOnFailure = true };

        Assert.Equal(23, await fixture.RunAsync());

        Assert.Equal(1, fixture.Window.CloseCount);
        Assert.Equal(["connect"], fixture.Calls);
    }

    [Fact]
    public async Task Cancellation_DuringConnect_StartsNoFurtherPhaseAndStillReleasesKeyAndWindow()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture { OnConnect = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunWithTokenAsync(cancellation.Token));

        Assert.Equal(["connect"], fixture.Calls);
        Assert.Equal(1, fixture.Window.CloseCount);
        Assert.False(fixture.Key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task Cancellation_DuringSystemPreparation_NeverStartsDeploy()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture { OnSystemPreparation = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunWithTokenAsync(cancellation.Token));

        Assert.Equal(["connect", "system"], fixture.Calls);
        Assert.Equal(1, fixture.Window.CloseCount);
        Assert.False(fixture.Key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task IsPhaseRunning_IsTrueOnlyWhileAPhaseRuns()
    {
        var fixture = new Fixture();
        WinPeUiSession? session = null;
        var observed = new List<bool>();
        fixture.OnConnect = () => observed.Add(session!.IsPhaseRunning);
        fixture.OnSystemPreparation = () => observed.Add(session!.IsPhaseRunning);
        fixture.Deploy = _ =>
        {
            observed.Add(session!.IsPhaseRunning);
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };
        session = fixture.CreateSession();

        await session.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([true, false, true], observed);
        Assert.False(session.IsPhaseRunning);
    }

    private sealed class FakeShellHost : IShellHost
    {
        public System.Windows.Window Window => throw new NotSupportedException();

        public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested
        {
            add { }
            remove { }
        }

        public event EventHandler? FirstContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(System.Windows.FrameworkElement content) => throw new NotSupportedException();
    }

    private sealed class Fixture : IConnectPhaseRunner, ISystemPreparationStep, IDeployPhaseRunner, IUiStateWriter, IUiReadinessReporter
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        internal List<string> Calls { get; } = [];
        internal List<UiState> States { get; } = [];
        internal List<string> Readiness { get; } = [];
        internal IReadOnlyList<LogEvent> Events => [.. events];
        internal FakeWindow Window { get; } = new();
        internal DeploymentKeyTransfer Key { get; } = DeploymentKeyTransfer.FromKey(new byte[DeploymentKeyTransfer.KeyLength]);
        internal int ConnectExitCode { get; init; }
        internal bool ConnectRenders { get; init; } = true;
        internal bool HasWindow { get; init; } = true;
        internal bool WindowOnFailure { get; init; }
        internal bool SystemPreparationFails { get; init; }
        internal bool IsWinPe { get; init; } = true;
        internal bool SkipConnect { get; init; }
        internal int DeployExitCode { get; init; }
        internal IUiStateWriter? StateWriter { get; init; }
        internal Func<DeployPhaseOptions, Task<int>>? Deploy { get; set; }
        internal Action? OnConnect { get; set; }
        internal Action? OnSystemPreparation { get; set; }
        internal DeployPhaseOptions? DeployOptions { get; private set; }

        internal WinPeUiSession CreateSession() => new(this, this, this, StateWriter ?? this, this, () => IsWinPe,
            new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegatingSink(events.Enqueue)).CreateLogger(),
            SkipConnect);

        internal Task<int> RunAsync() => CreateSession().RunAsync(TestContext.Current.CancellationToken);

        internal Task<int> RunWithTokenAsync(CancellationToken cancellationToken) => CreateSession().RunAsync(cancellationToken);

        Task<ConnectPhaseOutcome> IConnectPhaseRunner.RunAsync(Action onConfigurationLoaded, Action onFirstContentRendered,
            CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            OnConnect?.Invoke();
            onConfigurationLoaded();
            if (ConnectRenders) onFirstContentRendered();
            bool succeeded = ConnectExitCode == 0;
            return Task.FromResult(new ConnectPhaseOutcome(ConnectExitCode, Key, "Dark", "fr-FR",
                (succeeded || WindowOnFailure) && HasWindow ? Window : null));
        }

        Task ISystemPreparationStep.RunAsync(CancellationToken cancellationToken)
        {
            Calls.Add("system");
            OnSystemPreparation?.Invoke();
            return SystemPreparationFails ? Task.FromException(new HttpRequestException("Offline")) : Task.CompletedTask;
        }

        Task<int> IDeployPhaseRunner.RunAsync(Func<IShellHost, DeployPhaseOptions> createOptions, CancellationToken cancellationToken)
        {
            DeployPhaseOptions options = createOptions(new FakeShellHost());
            Calls.Add("deploy");
            DeployOptions = options;
            return Deploy?.Invoke(options) ?? Task.FromResult(DeployExitCode);
        }

        void IUiStateWriter.Write(UiState state) => States.Add(state);

        void IUiReadinessReporter.ReportConfigurationLoaded() => Readiness.Add("configuration_loaded");

        void IUiReadinessReporter.ReportUiReady() => Readiness.Add("ui_ready");

        void IUiReadinessReporter.ReportStartupFailed(int exitCode) => Readiness.Add($"startup_failed:{exitCode}");
    }

    private sealed class FakeWindow : IHandOverWindow
    {
        internal int CloseCount { get; private set; }

        public void Close() => CloseCount++;
    }
}
