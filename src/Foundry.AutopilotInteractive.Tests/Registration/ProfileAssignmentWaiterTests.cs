// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Tests.Authentication;
using Foundry.Core.Services.Autopilot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class ProfileAssignmentWaiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Wait_ReturnsAssigned_ForAssignedInSync()
    {
        Harness harness = new("assignedInSync");

        Assert.Equal(ProfileAssignmentOutcome.Assigned, await harness.WaitAsync());
        Assert.Empty(harness.Delays);
    }

    [Fact]
    public async Task Wait_KeepsPolling_ForPendingAndNotAssigned()
    {
        Harness harness = new("pending", "notAssigned", "ASSIGNEDOutOfSync");

        Assert.Equal(ProfileAssignmentOutcome.Assigned, await harness.WaitAsync());
        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)], harness.Delays);
        Assert.Equal(3, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task Wait_ReturnsFailed()
    {
        Assert.Equal(ProfileAssignmentOutcome.Failed, await new Harness("pending", "failed").WaitAsync());
    }

    [Fact]
    public async Task Wait_ReturnsUnavailable_OnHttpError()
    {
        Harness harness = new(HttpStatusCode.Forbidden);

        Assert.Equal(ProfileAssignmentOutcome.Unavailable, await harness.WaitAsync());
    }

    [Fact]
    public async Task Wait_ReturnsUnavailable_WhenPropertyMissing()
    {
        Harness harness = new((string?)null);

        Assert.Equal(ProfileAssignmentOutcome.Unavailable, await harness.WaitAsync());
    }

    [Fact]
    public async Task Wait_ReturnsTimedOut_AtStopTime()
    {
        Harness harness = new("pending", "pending", "pending", "pending", "pending");

        ProfileAssignmentOutcome outcome = await harness.WaitAsync(stopAtUtc: Start.AddSeconds(70));

        Assert.Equal(ProfileAssignmentOutcome.TimedOut, outcome);
        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)], harness.Delays);
        Assert.Equal(Start.AddSeconds(70), harness.Now);
    }

    [Fact]
    public async Task Wait_ReturnsSkipped_WhenCancelled()
    {
        Harness harness = new("pending", "pending");
        using CancellationTokenSource skip = new();
        harness.OnDelay = skip.Cancel;

        Assert.Equal(ProfileAssignmentOutcome.Skipped, await harness.WaitAsync(skip: skip));
    }

    [Fact]
    public void ComputeStopAt_UsesTheEarlierOfTwentyMinutesAndTheDeadlineMargin()
    {
        Assert.Equal(Start.AddMinutes(20), ProfileAssignmentWaiter.ComputeStopAt(Start, null));
        Assert.Equal(Start.AddMinutes(20), ProfileAssignmentWaiter.ComputeStopAt(Start, Start.AddHours(2)));
        Assert.Equal(Start.AddMinutes(4), ProfileAssignmentWaiter.ComputeStopAt(Start, Start.AddMinutes(5)));
    }

    private sealed class Harness
    {
        private readonly Queue<string?> _statuses;
        private readonly HttpStatusCode _failure;

        public Harness(params string?[] statuses)
        {
            _statuses = new Queue<string?>(statuses);
            Handler = new FakeHttpMessageHandler(Respond);
            Waiter = new ProfileAssignmentWaiter(
                new AutopilotGraphImportClient(
                    new HttpClient(Handler) { BaseAddress = new Uri("https://graph.microsoft.com/") },
                    NullLogger<AutopilotGraphImportClient>.Instance,
                    new AutopilotGraphImportClientOptions { RetryCount = 0, RetryDelay = TimeSpan.Zero }),
                DelayAsync,
                () => Now);
        }

        public Harness(HttpStatusCode failure)
            : this()
        {
            _failure = failure;
        }

        public FakeHttpMessageHandler Handler { get; }

        public ProfileAssignmentWaiter Waiter { get; }

        public DateTimeOffset Now { get; private set; } = Start;

        public List<TimeSpan> Delays { get; } = [];

        public Action? OnDelay { get; set; }

        public Task<ProfileAssignmentOutcome> WaitAsync(DateTimeOffset? stopAtUtc = null, CancellationTokenSource? skip = null) =>
            Waiter.WaitAsync("token", "device-id", stopAtUtc ?? Start.AddMinutes(20), skip?.Token ?? TestContext.Current.CancellationToken);

        private HttpResponseMessage Respond(HttpRequestMessage request, string body)
        {
            if (_failure != default)
            {
                return FakeHttpMessageHandler.Json(_failure, "{}");
            }

            string? status = _statuses.Dequeue();
            return FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                status is null ? "{ \"id\": \"device-id\" }" : $"{{ \"id\": \"device-id\", \"deploymentProfileAssignmentStatus\": \"{status}\" }}");
        }

        private Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            Now += delay;
            OnDelay?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
