// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Authentication;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Tests.Authentication;

public sealed class SignInFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_IncreasesIntervalBy5Seconds_OnSlowDown()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.SlowDown));
        harness.Client.Polls.Enqueue(Succeeded());

        AuthenticatedSession session = await harness.RunAsync();

        Assert.Equal("token", session.AccessToken);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], harness.Delays);
    }

    [Fact]
    public async Task RunAsync_RequestsNewCode_WhenCodeExpires()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        harness.Client.Codes.Enqueue(harness.NewCode("BBBB-2222"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.Expired));
        harness.Client.Polls.Enqueue(Succeeded());

        await harness.RunAsync();

        Assert.Equal(
            [
                (SignInPhase.RequestingCode, (string?)null),
                (SignInPhase.WaitingForUser, "AAAA-1111"),
                (SignInPhase.RequestingCode, null),
                (SignInPhase.WaitingForUser, "BBBB-2222")
            ],
            harness.Reports.Select(report => (report.Phase, report.Code?.UserCode)));
    }

    [Fact]
    public async Task RunAsync_RequestsNewCode_WhenClockPassesExpiry()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111", lifetime: TimeSpan.FromSeconds(7)));
        harness.Client.Codes.Enqueue(harness.NewCode("BBBB-2222"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.Pending));
        harness.Client.Polls.Enqueue(Succeeded());

        await harness.RunAsync();

        // The first 5 s delay is inside the 7 s lifetime and polls once; the second delay passes the expiry without polling.
        Assert.Equal(2, harness.Reports.Count(report => report.Phase == SignInPhase.WaitingForUser));
        Assert.Contains(harness.Reports, report => report.Phase == SignInPhase.RequestingCode && report.Code is null);
        Assert.Equal(2, harness.Client.PollCount);
    }

    [Fact]
    public async Task RunAsync_ReportsWaitingForNetwork_AndKeepsPolling_OnTransientFailure()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.TransientFailure));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.TransientFailure));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.Pending));
        harness.Client.Polls.Enqueue(Succeeded());

        AuthenticatedSession session = await harness.RunAsync();

        Assert.Equal("token", session.AccessToken);
        Assert.Equal(
            [SignInPhase.RequestingCode, SignInPhase.WaitingForUser, SignInPhase.WaitingForNetwork, SignInPhase.WaitingForUser],
            harness.Reports.Select(report => report.Phase));
        SignInState network = harness.Reports.First(report => report.Phase == SignInPhase.WaitingForNetwork);
        Assert.Equal("AAAA-1111", network.Code?.UserCode);
        Assert.Equal(
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)],
            harness.Delays);
    }

    [Fact]
    public async Task RunAsync_RenewsCodeExactlyAtExpiry_WhenRetryWaitReachesIt()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111", lifetime: TimeSpan.FromSeconds(12)));
        harness.Client.Codes.Enqueue(harness.NewCode("BBBB-2222"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.TransientFailure));
        harness.Client.Polls.Enqueue(Succeeded());

        await harness.RunAsync();

        // 5 s to the first poll (transient), then the 10 s retry wait is cut to the 7 s left; no poll happens after expiry.
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5)], harness.Delays);
        Assert.Equal(2, harness.Client.PollCount);
        Assert.Equal(
            [
                (SignInPhase.RequestingCode, (string?)null),
                (SignInPhase.WaitingForUser, "AAAA-1111"),
                (SignInPhase.WaitingForNetwork, "AAAA-1111"),
                (SignInPhase.RequestingCode, null),
                (SignInPhase.WaitingForUser, "BBBB-2222")
            ],
            harness.Reports.Select(report => (report.Phase, report.Code?.UserCode)));
    }

    [Fact]
    public async Task RunAsync_RetriesCodeRequest_OnTransientRequestFailure()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(new DeviceCodeRequestException(isTransient: true, errorCode: null));
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        harness.Client.Polls.Enqueue(Succeeded());

        await harness.RunAsync();

        Assert.Equal(
            [SignInPhase.RequestingCode, SignInPhase.WaitingForNetwork, SignInPhase.WaitingForUser],
            harness.Reports.Select(report => report.Phase));
        Assert.Null(harness.Reports[1].Code);
        Assert.Equal(TimeSpan.FromSeconds(10), harness.Delays[0]);
    }

    [Fact]
    public async Task RunAsync_Throws_OnBlockingError()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        harness.Client.Polls.Enqueue(new DeviceCodePollResult(DeviceCodePollStatus.Failed, ErrorCode: "access_denied"));

        SignInFailedException exception = await Assert.ThrowsAsync<SignInFailedException>(() => harness.RunAsync());

        Assert.Equal("access_denied", exception.ErrorCode);
        SignInState last = harness.Reports[^1];
        Assert.Equal(SignInPhase.Failed, last.Phase);
        Assert.Equal("access_denied", last.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_Throws_OnBlockingRequestError()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(new DeviceCodeRequestException(isTransient: false, errorCode: "unauthorized_client"));

        SignInFailedException exception = await Assert.ThrowsAsync<SignInFailedException>(() => harness.RunAsync());

        Assert.Equal("unauthorized_client", exception.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_ReportsFailedAndThrows_OnUnexpectedException()
    {
        CapturingLogger logger = new();
        Harness harness = new(logger);
        InvalidOperationException cause = new("boom");
        harness.Client.Codes.Enqueue(cause);

        SignInFailedException exception = await Assert.ThrowsAsync<SignInFailedException>(() => harness.RunAsync());

        Assert.Equal("unexpected_error", exception.ErrorCode);
        Assert.Same(cause, exception.InnerException);
        SignInState last = harness.Reports[^1];
        Assert.Equal(SignInPhase.Failed, last.Phase);
        Assert.Null(last.Code);
        Assert.Equal("unexpected_error", last.ErrorCode);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Information, logger.Entries[0]);
        Assert.Equal(LogLevel.Error, logger.Entries[1]);
    }

    [Fact]
    public async Task RunAsync_EndsQuietly_WhenCancelled()
    {
        Harness harness = new();
        harness.Client.Codes.Enqueue(harness.NewCode("AAAA-1111"));
        using CancellationTokenSource cts = new();
        harness.OnDelay = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(cts.Token));

        Assert.DoesNotContain(harness.Reports, report => report.Phase == SignInPhase.Failed);
    }

    private static DeviceCodePollResult Succeeded() =>
        new(DeviceCodePollStatus.Succeeded, new AuthenticatedSession("token", Start.AddHours(1), "admin@contoso.com"));

    private sealed class Harness
    {
        private DateTimeOffset _now = Start;

        public Harness(ILogger<SignInFlow>? logger = null)
        {
            Flow = new SignInFlow(
                Client,
                (delay, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Delays.Add(delay);
                    _now += delay;
                    OnDelay?.Invoke();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                () => _now,
                logger);
        }

        public FakeDeviceCodeClient Client { get; } = new();

        public SignInFlow Flow { get; }

        public List<TimeSpan> Delays { get; } = [];

        public List<SignInState> Reports { get; } = [];

        public Action? OnDelay { get; set; }

        public DeviceCodeSession NewCode(string userCode, TimeSpan? lifetime = null) =>
            new("device-" + userCode, userCode, new Uri("https://microsoft.com/devicelogin"), Start + (lifetime ?? TimeSpan.FromMinutes(15)), TimeSpan.FromSeconds(5));

        public Task<AuthenticatedSession> RunAsync(CancellationToken? ct = null) =>
            Flow.RunAsync(new SyncProgress(Reports), ct ?? TestContext.Current.CancellationToken);
    }

    private sealed class CapturingLogger : ILogger<SignInFlow>
    {
        public List<LogLevel> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(logLevel);
    }

    private sealed class SyncProgress(List<SignInState> reports) : IProgress<SignInState>
    {
        public void Report(SignInState value) => reports.Add(value);
    }

    private sealed class FakeDeviceCodeClient : IDeviceCodeClient
    {
        public Queue<object> Codes { get; } = new();

        public Queue<DeviceCodePollResult> Polls { get; } = new();

        public int PollCount { get; private set; }

        public Task<DeviceCodeSession> RequestAsync(CancellationToken ct)
        {
            object next = Codes.Dequeue();
            return next is Exception exception
                ? Task.FromException<DeviceCodeSession>(exception)
                : Task.FromResult((DeviceCodeSession)next);
        }

        public Task<DeviceCodePollResult> PollAsync(DeviceCodeSession session, CancellationToken ct)
        {
            PollCount++;
            return Task.FromResult(Polls.Dequeue());
        }
    }
}
