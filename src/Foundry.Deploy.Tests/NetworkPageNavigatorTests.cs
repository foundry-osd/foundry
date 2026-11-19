// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Network;
using Foundry.Deploy.Services.Security;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Tests;

public sealed class NetworkPageNavigatorTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(1, DeploymentKeyTransfer.KeyLength).Select(static value => (byte)value)];

    [Fact]
    public async Task OpenAsync_WithoutTheHostCallback_IsUnavailableAndDoesNothing()
    {
        var harness = new Harness(openNetworkPage: null);

        bool opened = await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.False(harness.Navigator.IsAvailable);
        Assert.False(opened);
        Assert.Equal(0, harness.ShowCount);
        Assert.Empty(harness.Logger.Entries);
    }

    [Fact]
    public async Task OpenAsync_WhenUnlocked_PassesACopyOfTheKeyAndTheCurrentPreferences()
    {
        NetworkPageRequest? received = null;
        byte[] taken = new byte[DeploymentKeyTransfer.KeyLength];
        var harness = new Harness((request, _) =>
        {
            received = request;
            Assert.True(request.DeploymentKey!.TryTake(taken));
            return Task.FromResult(new NetworkPageResult(null, null));
        });
        harness.KeySession.SetKey(Key);

        await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(Key, taken);
        Assert.Equal("Dark", received.ThemeMode);
        Assert.Equal("fr-FR", received.CultureName);
        Assert.True(harness.KeySession.IsUnlocked);
    }

    [Fact]
    public async Task OpenAsync_WhenLocked_PassesNoKey()
    {
        NetworkPageRequest? received = null;
        var harness = new Harness((request, _) =>
        {
            received = request;
            return Task.FromResult(new NetworkPageResult(null, null));
        });

        await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.NotNull(received);
        Assert.Null(received.DeploymentKey);
    }

    [Fact]
    public async Task OpenAsync_AfterThePageReturns_ShowsDeployAgainThenAppliesThePreferencesAndRefreshes()
    {
        var order = new List<string>();
        Harness? harness = null;
        harness = new Harness((_, _) =>
        {
            Assert.True(harness!.Navigator.IsOpen);
            order.Add("page");
            return Task.FromResult(new NetworkPageResult("Light", "de-DE"));
        }, order);

        bool opened = await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.True(opened);
        Assert.False(harness.Navigator.IsOpen);
        Assert.Equal(["page", "show", "preferences:Light/de-DE", "refresh"], order);
        Assert.Equal(2, harness.Logger.Entries.Count(static entry => entry.Level == LogLevel.Information));
    }

    [Fact]
    public async Task OpenAsync_WhenThePageFails_ShowsDeployAgainAndLogsOnlyTheFailureType()
    {
        var order = new List<string>();
        var harness = new Harness((_, _) => throw new InvalidOperationException("secret detail"), order);

        bool opened = await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.False(opened);
        Assert.False(harness.Navigator.IsOpen);
        Assert.Equal(["show"], order);
        (LogLevel level, string message) = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_WhileAPageIsOpen_ReturnsFalse()
    {
        var release = new TaskCompletionSource<NetworkPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness((_, _) => release.Task);

        Task<bool> first = harness.Navigator.OpenAsync(CancellationToken.None);
        bool second = await harness.Navigator.OpenAsync(CancellationToken.None);
        release.SetResult(new NetworkPageResult(null, null));

        Assert.False(second);
        Assert.True(await first);
        Assert.Equal(1, harness.ShowCount);
    }

    [Fact]
    public async Task OpenAsync_DuringTheReturnRefresh_IsClosedSoTheRefreshCanOpenThePageAgain()
    {
        int pages = 0;
        bool? isOpenDuringRefresh = null;
        bool? reopened = null;
        Harness? harness = null;
        harness = new Harness((_, _) =>
        {
            pages++;
            return Task.FromResult(new NetworkPageResult(null, null));
        }, refresh: async token =>
        {
            if (pages > 1)
            {
                return;
            }

            isOpenDuringRefresh = harness!.Navigator.IsOpen;
            reopened = await harness.Navigator.OpenAsync(token);
        });

        Assert.True(await harness.Navigator.OpenAsync(CancellationToken.None));

        Assert.False(isOpenDuringRefresh);
        Assert.True(reopened);
        Assert.Equal(2, pages);
        Assert.False(harness.Navigator.IsOpen);
    }

    [Fact]
    public async Task CancelAndWait_WaitsForTheRunWhoseRefreshReopenedThePage()
    {
        int pages = 0;
        var refreshEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopened = new TaskCompletionSource<Task<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Harness? harness = null;
        harness = new Harness((_, token) =>
        {
            if (++pages == 1)
            {
                return Task.FromResult(new NetworkPageResult(null, null));
            }

            var page = new TaskCompletionSource<NetworkPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => page.TrySetCanceled(token));
            return page.Task;
        }, refresh: async token =>
        {
            if (pages > 1)
            {
                return;
            }

            // The refresh reopens the page after a wait of its own, as a failure dialog does.
            await Task.Yield();
            Task<bool> reopen = harness!.Navigator.OpenAsync(token);
            reopened.SetResult(reopen);
            await reopen;
            await refreshEnd.Task;
        });

        Task<bool> open = harness.Navigator.OpenAsync(CancellationToken.None);
        Task<bool> reopen = await reopened.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Task wait = harness.Navigator.CancelAndWaitAsync();
        Assert.False(await reopen.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The first run is still in its refresh, so the wait goes on after the reopened page ended.
        Assert.NotSame(wait, await Task.WhenAny(wait, Task.Delay(200, TestContext.Current.CancellationToken)));
        refreshEnd.SetResult();
        await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(open.IsCompleted);
    }

    [Fact]
    public async Task CancelAndWait_DuringAReopen_CancelsThePageOnTheCallingThreadAndWaitsForIt()
    {
        int? cancellationThread = null;
        var pageEnded = new TaskCompletionSource<NetworkPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness((_, cancellationToken) =>
        {
            cancellationToken.Register(() => cancellationThread = Environment.CurrentManagedThreadId);
            return pageEnded.Task;
        });

        Task<bool> open = harness.Navigator.OpenAsync(CancellationToken.None);
        int callingThread = Environment.CurrentManagedThreadId;
        Task wait = harness.Navigator.CancelAndWaitAsync();

        Assert.Equal(callingThread, cancellationThread);
        Assert.False(wait.IsCompleted);
        pageEnded.SetException(new OperationCanceledException());
        await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(await open);
        Assert.False(harness.Navigator.IsOpen);
        Assert.Equal(0, harness.PreferenceCount);
        Assert.DoesNotContain(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task CancelAndWait_WhenAHostCancellationCallbackFails_LogsItsTypeAndStillWaitsForThePage()
    {
        var pageEnded = new TaskCompletionSource<NetworkPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness((_, cancellationToken) =>
        {
            cancellationToken.Register(static () => throw new InvalidOperationException("secret detail"));
            return pageEnded.Task;
        });

        Task<bool> open = harness.Navigator.OpenAsync(CancellationToken.None);
        Task wait = harness.Navigator.CancelAndWaitAsync();

        Assert.False(wait.IsCompleted);
        (_, string message) = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", message, StringComparison.Ordinal);
        pageEnded.SetException(new OperationCanceledException());
        await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(await open);
    }

    [Fact]
    public async Task OpenAsync_WhenThePageAndTheReturnToDeployBothFail_LogsBothFailureTypes()
    {
        var harness = new Harness(
            (_, _) => throw new InvalidOperationException(),
            showDeployContent: static () => throw new NotSupportedException());

        bool opened = await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.False(opened);
        Assert.False(harness.Navigator.IsOpen);
        (_, string message) = Assert.Single(harness.Logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.Contains(nameof(NotSupportedException), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_RefusesLaterRequests()
    {
        int calls = 0;
        var harness = new Harness((_, _) =>
        {
            calls++;
            return Task.FromResult(new NetworkPageResult(null, null));
        });

        harness.Navigator.Dispose();
        harness.Navigator.Dispose();

        Assert.False(await harness.Navigator.OpenAsync(CancellationToken.None));
        await harness.Navigator.CancelAndWaitAsync();
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task OpenAsync_AfterThePhaseEnded_ReturnsFalseWithoutCallingTheHost()
    {
        int calls = 0;
        var harness = new Harness((_, _) =>
        {
            calls++;
            return Task.FromResult(new NetworkPageResult(null, null));
        });

        await harness.Navigator.CancelAndWaitAsync();
        bool opened = await harness.Navigator.OpenAsync(CancellationToken.None);

        Assert.False(opened);
        Assert.Equal(0, calls);
    }

    private sealed class Harness
    {
        public Harness(
            Func<NetworkPageRequest, CancellationToken, Task<NetworkPageResult>>? openNetworkPage,
            List<string>? order = null,
            Action? showDeployContent = null,
            Func<CancellationToken, Task>? refresh = null)
        {
            order ??= [];
            Navigator = new NetworkPageNavigator(
                openNetworkPage,
                KeySession,
                new AutopilotInteractiveTestRuntime.FixedTheme(ThemeMode.Dark),
                new AutopilotInteractiveTestRuntime.FixedCulture("fr-FR"),
                () =>
                {
                    ShowCount++;
                    order.Add("show");
                    showDeployContent?.Invoke();
                },
                (theme, culture) =>
                {
                    PreferenceCount++;
                    order.Add($"preferences:{theme}/{culture}");
                },
                refresh is null ? new RecordingRefresh(order) : new DelegateRefresh(refresh),
                Logger);
        }

        public DeploymentSecretKeySession KeySession { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public NetworkPageNavigator Navigator { get; }

        public int ShowCount { get; private set; }

        public int PreferenceCount { get; private set; }
    }

    private sealed class RecordingRefresh(List<string> order) : INetworkReturnRefresh
    {
        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            order.Add("refresh");
            return Task.CompletedTask;
        }
    }

    private sealed class DelegateRefresh(Func<CancellationToken, Task> refresh) : INetworkReturnRefresh
    {
        public Task RefreshAsync(CancellationToken cancellationToken) => refresh(cancellationToken);
    }

    private sealed class RecordingLogger : ILogger<NetworkPageNavigator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
