using System.Net;
using System.Text.Json;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogTelemetryServiceTests
{
    [Fact]
    public async Task TrackAsync_WhenHttpCaptureFails_DoesNotThrow()
    {
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler { ThrowOnSend = true });
        var service = CreateService(httpClient);

        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?> { ["boot_media_target"] = "iso" });
    }

    [Fact]
    public async Task TrackAsync_WhenTelemetryDisabled_DoesNotSend()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var options = new TelemetryOptions(false, TelemetryDefaults.PostHogEuHost, "project-token", "install-id");
        var service = CreateService(httpClient, options);

        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?> { ["boot_media_target"] = "iso" });

        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task TrackAsync_SendsCapturePayloadWithoutClientTimestamp()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        await service.TrackAsync(
            TelemetryEvents.OsdBootMediaFinished,
            new Dictionary<string, object?>
            {
                ["boot_media_target"] = "iso",
                ["boot_media_architecture"] = "arm64",
                ["failed_step_name"] = "Customize boot image",
                ["ssid"] = "CorpWifi"
            });

        Assert.Equal("https://eu.i.posthog.com/i/v0/e/", handler.RequestUri?.ToString());

        JsonElement root = handler.ReadJson();
        Assert.Equal("project-token", root.GetProperty("api_key").GetString());
        Assert.Equal(TelemetryEvents.OsdBootMediaFinished, root.GetProperty("event").GetString());
        Assert.Equal("install-id", root.GetProperty("distinct_id").GetString());
        Assert.False(root.TryGetProperty("timestamp", out _));

        JsonElement properties = root.GetProperty("properties");
        Assert.False(properties.TryGetProperty("timestamp", out _));
        Assert.Equal("iso", properties.GetProperty("boot_media_target").GetString());
        Assert.Equal("arm64", properties.GetProperty("boot_media_architecture").GetString());
        Assert.Equal("Customize boot image", properties.GetProperty("failed_step_name").GetString());
        Assert.False(properties.TryGetProperty("ssid", out _));
        Assert.Equal(TelemetryApps.FoundryOsd, properties.GetProperty("app").GetString());
        Assert.Equal("1.2.3", properties.GetProperty("app_version").GetString());
        Assert.Equal("x64", properties.GetProperty("runtime_architecture").GetString());
        Assert.False(properties.TryGetProperty("architecture", out _));
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.False(properties.GetProperty("$geoip_disable").GetBoolean());
    }

    [Fact]
    public async Task TrackAsync_WhenEventNameIsUnknown_DoesNotSend()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        await service.TrackAsync("unknown_event", new Dictionary<string, object?> { ["success"] = true });

        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task FlushAsync_DoesNotSendAdditionalRequests()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        await service.FlushAsync();

        Assert.Equal(0, handler.SendCount);
    }

    [Fact]
    public async Task NullTelemetryService_DoesNotThrow()
    {
        var service = new NullTelemetryService();

        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?> { ["boot_media_target"] = "iso" });
        await service.FlushAsync();
    }

    private static PostHogTelemetryService CreateService(HttpClient httpClient, TelemetryOptions? options = null)
    {
        options ??= new TelemetryOptions(true, TelemetryDefaults.PostHogEuHost, "project-token", "install-id");
        var context = new TelemetryContext(
            TelemetryApps.FoundryOsd,
            "1.2.3",
            "debug",
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            TelemetryBootMediaTargets.Usb,
            "x64",
            "en-US",
            "session-id");

        return new PostHogTelemetryService(httpClient, options, context);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public bool ThrowOnSend { get; init; }

        public int SendCount { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowOnSend)
            {
                throw new HttpRequestException("capture failed");
            }

            SendCount++;
            RequestUri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public JsonElement ReadJson()
        {
            Assert.NotNull(Body);
            using JsonDocument document = JsonDocument.Parse(Body);
            return document.RootElement.Clone();
        }
    }
}
