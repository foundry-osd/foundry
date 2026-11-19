// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Globalization;
using Foundry.Connect;
using Foundry.Connect.Network;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy;
using Foundry.Deploy.Services.Network;
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
        Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
        Assert.Equal([UiPhase.Connect, UiPhase.System, UiPhase.Deploy], fixture.States.Select(state => state.Phase));
        Assert.All(fixture.States, state => Assert.False(state.DestructiveStarted));
        DeployPhaseOptions options = fixture.DeployOptions!;
        Assert.Same(fixture.Key, options.InitialDeploymentKey);
        Assert.Equal("Dark", options.ThemeMode);
        Assert.Equal("fr-FR", options.CultureName);
    }

    [Fact]
    public async Task Culture_IsRecordedAtConnectStartAfterALanguageChangeAndAtDeployStart()
    {
        var fixture = new Fixture();
        fixture.OnConnect = () =>
        {
            fixture.ConnectOptions!.OnCultureChanged!("en-US");
            fixture.ConnectOptions.OnCultureChanged("de-DE");
        };
        fixture.Deploy = options =>
        {
            options.OnCultureChanged!("fr-FR");
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        await fixture.RunAsync();

        Assert.Equal(
            [
                (UiPhase.Connect, (string?)null),
                (UiPhase.Connect, "en-US"),
                (UiPhase.Connect, "de-DE"),
                (UiPhase.System, "de-DE"),
                // Deploy starts with the culture Connect ended with, then reports its own.
                (UiPhase.Deploy, "fr-FR"),
                (UiPhase.Deploy, "fr-FR")
            ],
            fixture.States.Select(state => (state.Phase, state.Culture)));
    }

    [Fact]
    public async Task Culture_ChangeDuringDeploy_KeepsTheDestructiveFlag()
    {
        var fixture = new Fixture();
        fixture.Deploy = options =>
        {
            options.OnDestructiveStepStarted();
            options.OnCultureChanged!("it-IT");
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        await fixture.RunAsync();

        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true, Culture: "it-IT"), fixture.States[^1]);
    }

    [Fact]
    public async Task BothPhases_ShowTheirContentInTheSameShellAndTheSessionNeverClosesIt()
    {
        var fixture = new Fixture();
        fixture.Deploy = options =>
        {
            options.Shell.ShowContent(null!);
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };
        fixture.OnConnect = () => fixture.Shell.ShowContent(null!);

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Same(fixture.Shell, fixture.ConnectOptions!.Shell);
        Assert.Equal(WinPeUiSession.StatusPageTitle, fixture.ConnectOptions.StatusPageTitle);
        Assert.Same(fixture.Shell, fixture.DeployOptions!.Shell);
        Assert.Equal(3, fixture.Shell.ShowCount);
        Assert.Equal(0, fixture.Shell.WindowAccessCount);
    }

    [Fact]
    public async Task CloseRequest_DuringSystemPreparation_IsRefusedAndDeployStillRuns()
    {
        var fixture = new Fixture();
        bool? accepted = null;
        fixture.OnSystemPreparation = () => accepted = fixture.Shell.RequestClose();

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.False(accepted);
        Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
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
    public async Task ConnectEndingBeforeItsFirstContent_ReportsStartupFailureOnlyForFailures(int connectExitCode, bool failed)
    {
        var fixture = new Fixture { ConnectExitCode = connectExitCode, ConnectRenders = false };

        await fixture.RunAsync();

        Assert.Equal(failed ? ["configuration_loaded", $"startup_failed:{connectExitCode}"] : ["configuration_loaded"],
            fixture.Readiness);
    }

    [Fact]
    public async Task ConnectFailureAfterItsFirstContent_NeverReportsStartupFailure()
    {
        var fixture = new Fixture { ConnectExitCode = 23 };

        await fixture.RunAsync();

        Assert.Equal(["configuration_loaded", "ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task Readiness_IsReportedOnceAtConnectsFirstContentAndNotAgainWhenDeploysContentRenders()
    {
        // Each phase calls its hook once (Connect: its prompt or its page; Deploy: its own content in the same shell).
        var fixture = new Fixture();
        int deployHookCalls = 0;
        fixture.Deploy = options =>
        {
            options.OnFirstContentRendered();
            deployHookCalls++;
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        await fixture.RunAsync();

        Assert.Equal(1, deployHookCalls);
        Assert.Equal(["configuration_loaded", "ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task SystemPreparation_ShowsTheStatusPageInConnectsLanguageBetweenConnectAndDeployForAtLeastTheMinimumTime()
    {
        var fixture = new Fixture();
        int shown = -1;
        fixture.OnSystemPreparation = () => shown = fixture.Shell.ShowCount;
        // An English process language proves the page does not fall back to it: the result does not depend on the machine.
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            await fixture.RunAsync();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
        // The page is on screen before the preparation starts.
        Assert.Equal(1, shown);
        StatusPageModel model = Assert.Single(fixture.StatusPages);
        // The fixture's Connect ends in French, the language the page follows.
        Assert.Equal("Réglage de l'horloge et du fuseau horaire…", model.Status);
        Assert.Equal(WinPeUiSession.StatusPageTitle, model.Title);
        Assert.Equal(model.Title, fixture.ConnectOptions!.StatusPageTitle);
    }

    [Fact]
    public async Task StatusPage_IsNotShownOutsideWindowsPeOrWhenConnectFailsOrIsSkipped()
    {
        var outside = new Fixture { IsWinPe = false };
        await outside.RunAsync();
        var failed = new Fixture { ConnectExitCode = 21 };
        await failed.RunAsync();
        var skipped = new Fixture { SkipConnect = true };
        await skipped.RunAsync();

        Assert.Empty(outside.StatusPages);
        Assert.Empty(failed.StatusPages);
        Assert.Empty(skipped.StatusPages);
    }

    [Fact]
    public async Task ClockUsable_IsRecordedInTheDeployStateAfterASuccessfulSync()
    {
        var fixture = new Fixture { ClockUsableAfterPreparation = true };

        await fixture.RunAsync();

        Assert.Equal([false, false, true], fixture.States.Select(state => state.ClockUsable));
        Assert.Equal([UiPhase.Connect, UiPhase.System, UiPhase.Deploy], fixture.States.Select(state => state.Phase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClockUsable_StaysFalseWhenTheSyncFailsAndDeployStillStarts(bool throws)
    {
        // Review focus 4: no HTTPS time source. The step either reports an unusable clock or fails.
        var fixture = new Fixture { SystemPreparationFails = throws, ClockUsableAfterPreparation = false };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
        Assert.All(fixture.States, state => Assert.False(state.ClockUsable));
    }

    [Fact]
    public async Task ClockUsable_SurvivesLaterStateWrites()
    {
        var fixture = new Fixture { ClockUsableAfterPreparation = true };
        fixture.Deploy = options =>
        {
            options.OnCultureChanged!("it-IT");
            options.OnDestructiveStepStarted();
            return Task.FromResult(DeployPhase.SuccessExitCode);
        };

        await fixture.RunAsync();

        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true, Culture: "it-IT", ClockUsable: true), fixture.States[^1]);
    }

    [Fact]
    public async Task SystemPreparationFailure_IsAWarningAndDeployStillStarts()
    {
        var fixture = new Fixture { SystemPreparationFails = true };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
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
                Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: false, Culture: "fr-FR"), UiStateFile.Read(path));
                options.OnDestructiveStepStarted();
                recorded = UiStateFile.Read(path);
                return Task.FromResult(DeployPhase.SuccessExitCode);
            };

            await fixture.RunAsync();

            Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true, Culture: "fr-FR"), recorded);
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
            Assert.Equal(["connect", "status", "system", "deploy"], fixture.Calls);
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
        // Without Connect, Deploy's content is the session's first content.
        Assert.Equal(["ui_ready"], fixture.Readiness);
    }

    [Fact]
    public async Task Cancellation_DuringConnect_StartsNoFurtherPhaseAndStillReleasesTheKey()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture { OnConnect = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunWithTokenAsync(cancellation.Token));

        Assert.Equal(["connect"], fixture.Calls);
        Assert.False(fixture.Key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task Cancellation_DuringSystemPreparation_NeverStartsDeploy()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture { OnSystemPreparation = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunWithTokenAsync(cancellation.Token));

        Assert.Equal(["connect", "status", "system"], fixture.Calls);
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

    [Fact]
    public async Task NetworkPage_ReopensConnectWithTheFirstProvisioningOutcomeAndReturnsItsPreferences()
    {
        var fixture = new Fixture { ClockUsableAfterPreparation = true };
        fixture.ReopenNetworkPageInDeploy();

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        // A usable clock is not prepared again.
        Assert.Equal(["connect", "status", "system", "deploy", "reconnect"], fixture.Calls);
        ConnectPhaseOptions options = fixture.RevisitOptions!;
        Assert.Same(fixture.FirstProvisioning, options.Revisit!.PreviousProvisioning);
        Assert.Same(fixture.RequestKey, options.Revisit.DeploymentKey);
        Assert.Same(fixture.Shell, options.Shell);
        Assert.Equal(WinPeUiSession.StatusPageTitle, options.StatusPageTitle);
        Assert.Equal("Light", options.ThemeMode);
        Assert.Equal("it-IT", options.CultureName);
        Assert.Null(options.OnConfigurationLoaded);
        Assert.Equal(new NetworkPageResult("Dark", "de-DE"), fixture.PageResult);
        Assert.Equal(2, fixture.Events.Count(item => item.Level == LogEventLevel.Information &&
            item.MessageTemplate.Text.Contains("Network menu", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(23)]
    public async Task NetworkPage_PreparesTheSystemAgainWhileTheClockIsUnusable(int revisitExitCode)
    {
        var fixture = new Fixture { RevisitExitCode = revisitExitCode };
        fixture.OnRevisit = () => fixture.ClockUsableAfterPreparation = true;
        fixture.ReopenNetworkPageInDeploy();

        await fixture.RunAsync();

        Assert.Equal(["connect", "status", "system", "deploy", "reconnect", "status", "system"], fixture.Calls);
        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: false, Culture: "fr-FR", ClockUsable: true), fixture.States[^1]);
        // A page that did not succeed leaves Deploy's theme and language.
        Assert.Equal(revisitExitCode == 0 ? new NetworkPageResult("Dark", "de-DE") : new NetworkPageResult(null, null),
            fixture.PageResult);
    }

    [Fact]
    public async Task NetworkPage_StaysInTheDeployPhaseAndReportsNoReadinessAgain()
    {
        var fixture = new Fixture { ClockUsableAfterPreparation = true };
        WinPeUiSession? session = null;
        bool? phaseRunning = null;
        fixture.OnRevisit = () =>
        {
            fixture.RevisitOptions!.OnCultureChanged!("de-DE");
            phaseRunning = session!.IsPhaseRunning;
        };
        fixture.ReopenNetworkPageInDeploy();
        session = fixture.CreateSession();

        await session.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(phaseRunning);
        Assert.Equal([UiPhase.Connect, UiPhase.System, UiPhase.Deploy, UiPhase.Deploy], fixture.States.Select(state => state.Phase));
        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: false, Culture: "de-DE", ClockUsable: true), fixture.States[^1]);
        Assert.Equal(["configuration_loaded", "ui_ready"], fixture.Readiness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkPage_ThatDoesNotSucceedAfterALanguageChange_RecordsDeploysLanguageAgain(bool connectThrows)
    {
        var fixture = new Fixture { ClockUsableAfterPreparation = true, RevisitExitCode = 20 };
        fixture.OnRevisit = () => fixture.RevisitOptions!.OnCultureChanged!("de-DE");
        if (connectThrows)
        {
            fixture.Revisit = (_, _) => Task.FromException<ConnectPhaseResult>(new InvalidOperationException("Revisit failed."));
        }

        fixture.ReopenNetworkPageInDeploy();

        await fixture.RunAsync();

        // Deploy keeps its own language (it-IT in the request) when the page did not succeed.
        Assert.Equal(["fr-FR", "de-DE", "it-IT"], fixture.States.Where(state => state.Phase == UiPhase.Deploy).Select(state => state.Culture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkPage_DisposesTheRequestKeyWhateverConnectDoes(bool connectThrows)
    {
        var fixture = new Fixture();
        if (connectThrows)
        {
            fixture.Revisit = (_, _) => Task.FromException<ConnectPhaseResult>(new InvalidOperationException("Revisit failed."));
        }

        fixture.ReopenNetworkPageInDeploy();

        await fixture.RunAsync();

        Assert.Equal(connectThrows, fixture.PageFailure is InvalidOperationException);
        Assert.False(fixture.RequestKey.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task NetworkPage_CancelledByDeploy_EndsTheReopenedConnectWithoutPreparingTheSystemAgain()
    {
        var fixture = new Fixture();
        using var deployCancellation = new CancellationTokenSource();
        fixture.Revisit = (_, token) =>
        {
            var aborted = new TaskCompletionSource<ConnectPhaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => aborted.TrySetResult(new ConnectPhaseResult(20, null, "Dark", "de-DE")));
            return aborted.Task;
        };
        bool pageWasOpen = false;
        fixture.Deploy = async options =>
        {
            Task<NetworkPageResult> page = options.OpenNetworkPage!(
                new NetworkPageRequest(fixture.RequestKey, "Light", "it-IT"), deployCancellation.Token);
            pageWasOpen = !page.IsCompleted;
            await deployCancellation.CancelAsync();
            await page;
            return DeployPhase.SuccessExitCode;
        };

        Assert.Equal(DeployPhase.SuccessExitCode, await fixture.RunAsync());

        Assert.True(pageWasOpen);
        Assert.Equal(["connect", "status", "system", "deploy", "reconnect"], fixture.Calls);
        Assert.False(fixture.RequestKey.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task NetworkPage_AfterTheDebugSkipOfConnect_HasNoPreviousProvisioningOutcome()
    {
        var fixture = new Fixture { SkipConnect = true, ClockUsableAfterPreparation = true };
        fixture.ReopenNetworkPageInDeploy();

        await fixture.RunAsync();

        ConnectRevisit revisit = fixture.RevisitOptions!.Revisit!;
        Assert.Null(revisit.PreviousProvisioning);
        Assert.Same(fixture.RequestKey, revisit.DeploymentKey);
    }

    /// <summary>
    /// The shared shell as the session sees it: it counts the contents shown, refuses a close request without a handler as
    /// the WinPE shell window does, and records any access to its window, which the session must never need.
    /// </summary>
    private sealed class FakeShellHost : IShellHost
    {
        internal int ShowCount { get; private set; }

        internal int WindowAccessCount { get; private set; }

        public System.Windows.Window Window
        {
            get
            {
                WindowAccessCount++;
                throw new NotSupportedException("The session never touches the shell window.");
            }
        }

        public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested;

        public event EventHandler<ShellContentRenderedEventArgs>? ShellContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(System.Windows.FrameworkElement content) => ShowCount++;

        /// <summary>Raises a close request and returns whether the window would close.</summary>
        internal bool RequestClose()
        {
            EventHandler<System.ComponentModel.CancelEventArgs>? handler = CloseRequested;
            if (handler is null)
            {
                return false;
            }

            var args = new System.ComponentModel.CancelEventArgs();
            handler(this, args);
            return !args.Cancel;
        }
    }

    /// <summary>A clock whose timers fire at once, so the status page minimum display time costs nothing in a test.</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan) { ThreadPool.QueueUserWorkItem(_ => callback(state)); }
            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture : IConnectPhaseRunner, ISystemPreparationStep, IDeployPhaseRunner, IUiStateWriter, IUiReadinessReporter
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        internal List<string> Calls { get; } = [];
        internal List<UiState> States { get; } = [];
        internal List<string> Readiness { get; } = [];
        internal IReadOnlyList<LogEvent> Events => [.. events];
        internal FakeShellHost Shell { get; } = new();
        internal DeploymentKeyTransfer Key { get; } = DeploymentKeyTransfer.FromKey(new byte[DeploymentKeyTransfer.KeyLength]);
        internal int ConnectExitCode { get; init; }
        internal bool ConnectRenders { get; init; } = true;
        internal bool SystemPreparationFails { get; init; }
        internal bool ClockUsableAfterPreparation { get; set; }
        internal ProvisioningOutcome FirstProvisioning { get; } =
            new(Dot1xState.NotConfigured, ProvisionedWifiState.NotConfigured, null, []);
        internal int RevisitExitCode { get; init; }
        internal Func<ConnectPhaseOptions, CancellationToken, Task<ConnectPhaseResult>>? Revisit { get; set; }
        internal Action? OnRevisit { get; set; }
        internal ConnectPhaseOptions? RevisitOptions { get; private set; }
        internal DeploymentKeyTransfer RequestKey { get; } = DeploymentKeyTransfer.FromKey(new byte[DeploymentKeyTransfer.KeyLength]);
        internal NetworkPageResult? PageResult { get; private set; }
        internal Exception? PageFailure { get; private set; }
        internal List<StatusPageModel> StatusPages { get; } = [];
        internal bool IsWinPe { get; init; } = true;
        internal bool SkipConnect { get; init; }
        internal int DeployExitCode { get; init; }
        internal IUiStateWriter? StateWriter { get; init; }
        internal Func<DeployPhaseOptions, Task<int>>? Deploy { get; set; }
        internal Action? OnConnect { get; set; }
        internal Action? OnSystemPreparation { get; set; }
        internal ConnectPhaseOptions? ConnectOptions { get; private set; }
        internal DeployPhaseOptions? DeployOptions { get; private set; }

        internal WinPeUiSession CreateSession() => new(Shell, this, this, this, StateWriter ?? this, this, () => IsWinPe,
            new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new DelegatingSink(events.Enqueue)).CreateLogger(),
            SkipConnect, model =>
            {
                Calls.Add("status");
                StatusPages.Add(model);
                return null!;
            }, new ImmediateTimeProvider());

        internal Task<int> RunAsync() => CreateSession().RunAsync(TestContext.Current.CancellationToken);

        internal Task<int> RunWithTokenAsync(CancellationToken cancellationToken) => CreateSession().RunAsync(cancellationToken);

        /// <summary>Makes Deploy open the network page once, with <see cref="RequestKey"/>, and record what it returned.</summary>
        internal void ReopenNetworkPageInDeploy() => Deploy = async options =>
        {
            try
            {
                PageResult = await options.OpenNetworkPage!(new NetworkPageRequest(RequestKey, "Light", "it-IT"),
                    TestContext.Current.CancellationToken);
            }
            catch (Exception exception)
            {
                // Deploy shows its own content again whatever the page did.
                PageFailure = exception;
            }

            return DeployPhase.SuccessExitCode;
        };

        Task<ConnectPhaseResult> IConnectPhaseRunner.RunAsync(ConnectPhaseOptions options, CancellationToken cancellationToken)
        {
            if (options.Revisit is not null)
            {
                Calls.Add("reconnect");
                RevisitOptions = options;
                // A reopened Connect never calls the readiness hooks; the no-op the session gives must stay harmless anyway.
                options.OnFirstContentRendered();
                OnRevisit?.Invoke();
                return Revisit?.Invoke(options, cancellationToken)
                    ?? Task.FromResult(new ConnectPhaseResult(RevisitExitCode, null, "Dark", "de-DE"));
            }

            Calls.Add("connect");
            ConnectOptions = options;
            OnConnect?.Invoke();
            options.OnConfigurationLoaded?.Invoke();
            if (ConnectRenders) options.OnFirstContentRendered();

            return Task.FromResult(new ConnectPhaseResult(ConnectExitCode, Key, "Dark", "fr-FR") { Provisioning = FirstProvisioning });
        }

        Task<bool> ISystemPreparationStep.RunAsync(CancellationToken cancellationToken)
        {
            Calls.Add("system");
            OnSystemPreparation?.Invoke();
            return SystemPreparationFails
                ? Task.FromException<bool>(new HttpRequestException("Offline"))
                : Task.FromResult(ClockUsableAfterPreparation);
        }

        Task<int> IDeployPhaseRunner.RunAsync(DeployPhaseOptions options, CancellationToken cancellationToken)
        {
            Calls.Add("deploy");
            DeployOptions = options;
            return Deploy?.Invoke(options) ?? Task.FromResult(DeployExitCode);
        }

        void IUiStateWriter.Write(UiState state) => States.Add(state);

        void IUiReadinessReporter.ReportConfigurationLoaded() => Readiness.Add("configuration_loaded");

        void IUiReadinessReporter.ReportUiReady() => Readiness.Add("ui_ready");

        void IUiReadinessReporter.ReportStartupFailed(int exitCode) => Readiness.Add($"startup_failed:{exitCode}");
    }
}
