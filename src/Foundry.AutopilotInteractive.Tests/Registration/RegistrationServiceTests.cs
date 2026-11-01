// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Tests.Authentication;
using Foundry.Core.Services.Autopilot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class RegistrationServiceTests
{
    private const string DeviceListJson = "{ \"value\": [ { \"id\": \"d1\", \"serialNumber\": \"SER1\", \"groupTag\": \"Sales\" } ] }";

    [Fact]
    public async Task Register_ReturnsHardwareIdentityUnavailable_WhenReaderThrows()
    {
        Harness harness = new(new ThrowingReader());

        RegistrationOutcome outcome = await harness.Service.RegisterAsync(
            new RegistrationRequest("token", "Sales", null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(RegistrationFailureCodes.HardwareIdentityUnavailable, outcome.FailureCode);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task Register_MapsForbidden_ToGraphForbidden()
    {
        Harness harness = new(new FixedReader(), (_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "{}"));

        RegistrationOutcome outcome = await harness.Service.RegisterAsync(
            new RegistrationRequest("token", null, null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None);

        Assert.Equal(RegistrationFailureCodes.GraphForbidden, outcome.FailureCode);
    }

    [Fact]
    public async Task Register_PropagatesUnauthorized()
    {
        Harness harness = new(new FixedReader(), (_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => harness.Service.RegisterAsync(
            new RegistrationRequest("token", null, null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsPropertiesNotApplied_AndKeepsIdentifiers_WhenPropertyUpdateFails()
    {
        Harness harness = new(new FixedReader(), RespondToPropertyUpdate(HttpStatusCode.BadRequest));

        RegistrationOutcome outcome = await harness.Service.RegisterAsync(
            new RegistrationRequest("token", "Marketing", null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(RegistrationFailureCodes.PropertiesNotApplied, outcome.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(outcome.ImportId));
        Assert.Equal("d1", outcome.AutopilotDeviceId);
    }

    [Fact]
    public async Task Register_MapsForbiddenPropertyUpdate_ToGraphForbidden()
    {
        Harness harness = new(new FixedReader(), RespondToPropertyUpdate(HttpStatusCode.Forbidden));

        RegistrationOutcome outcome = await harness.Service.RegisterAsync(
            new RegistrationRequest("token", "Marketing", null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None);

        Assert.Equal(RegistrationFailureCodes.GraphForbidden, outcome.FailureCode);
    }

    [Fact]
    public async Task Register_PropagatesUnauthorizedPropertyUpdate()
    {
        Harness harness = new(new FixedReader(), RespondToPropertyUpdate(HttpStatusCode.Unauthorized));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => harness.Service.RegisterAsync(
            new RegistrationRequest("token", "Marketing", null, null),
            new Progress<AutopilotHardwareHashUploadProgress>(),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Load_SetsGroupTagsUnavailable_WhenTagDiscoveryFails()
    {
        // The tag request is sent first; the device lookup request follows.
        int calls = 0;
        Harness harness = new(new FixedReader(), (_, _) => Interlocked.Increment(ref calls) == 1
            ? FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, DeviceListJson));

        RegistrationContext context = await harness.Service.LoadAsync("token", CancellationToken.None);

        Assert.True(context.GroupTagsUnavailable);
        Assert.Empty(context.GroupTags);
        Assert.Equal(AutopilotDeviceLookupStatus.Found, context.Lookup.Status);
    }

    [Fact]
    public async Task Load_ReturnsNotFound_WhenLookupFails()
    {
        int calls = 0;
        Harness harness = new(new FixedReader(), (_, _) => Interlocked.Increment(ref calls) == 1
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, DeviceListJson)
            : FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        RegistrationContext context = await harness.Service.LoadAsync("token", CancellationToken.None);

        Assert.False(context.GroupTagsUnavailable);
        Assert.Equal(["Sales"], context.GroupTags);
        Assert.Equal(AutopilotDeviceLookupStatus.NotFound, context.Lookup.Status);
    }

    [Fact]
    public async Task Load_PropagatesUnauthorized()
    {
        Harness harness = new(new FixedReader(), (_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => harness.Service.LoadAsync("token", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    // The device becomes visible with the tag Sales; the update to another tag answers with the given status.
    private static Func<HttpRequestMessage, string, HttpResponseMessage> RespondToPropertyUpdate(HttpStatusCode updateStatus) =>
        (request, _) => request.RequestUri!.AbsolutePath switch
        {
            string path when path.EndsWith("/import", StringComparison.Ordinal) => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                "{ \"value\": [ { \"id\": \"imported-1\", \"state\": { \"deviceImportStatus\": \"complete\" } } ] }"),
            string path when path.EndsWith("/updateDeviceProperties", StringComparison.Ordinal) => FakeHttpMessageHandler.Json(updateStatus, "{}"),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, DeviceListJson)
        };

    private sealed class Harness
    {
        public Harness(IHardwareIdentityReader reader, Func<HttpRequestMessage, string, HttpResponseMessage>? respond = null)
        {
            Handler = new FakeHttpMessageHandler(respond ?? ((_, _) => throw new InvalidOperationException("No Graph call expected.")));
            Service = new RegistrationService(
                new AutopilotGraphImportClient(
                    new HttpClient(Handler) { BaseAddress = new Uri("https://graph.microsoft.com/") },
                    NullLogger<AutopilotGraphImportClient>.Instance,
                    new AutopilotGraphImportClientOptions { RetryCount = 0, RetryDelay = TimeSpan.Zero, PollInterval = TimeSpan.Zero }),
                reader,
                NullLogger<RegistrationService>.Instance);
        }

        public FakeHttpMessageHandler Handler { get; }

        public RegistrationService Service { get; }
    }

    private sealed class ThrowingReader : IHardwareIdentityReader
    {
        public HardwareIdentity Read() => throw new HardwareIdentityUnavailableException("The hardware hash is not available.");
    }

    private sealed class FixedReader : IHardwareIdentityReader
    {
        public HardwareIdentity Read() => new("SER1", "aGFzaA==");
    }
}
