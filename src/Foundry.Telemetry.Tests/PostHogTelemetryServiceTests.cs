// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.Json;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

[Collection(RemoteDiagnosticsSinkCollection.Name)]
public sealed class PostHogTelemetryServiceTests
{
    [Fact]
    public void TelemetryContextFactory_Create_UsesCurrentDiagnosticSessionId()
    {
        TelemetryContext telemetryContext = TelemetryContextFactory.Create(
            TelemetryApps.FoundryOsd,
            "1.2.3",
            "debug",
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            TelemetryBootMediaTargets.Usb,
            "x64",
            "en-US");

        Assert.Equal(DiagnosticSessionContext.CurrentSessionId, telemetryContext.SessionId);

        RemoteDiagnosticsContext remoteContext = TelemetryContextFactory.CreateRemoteDiagnosticsContext(telemetryContext);
        Assert.Equal(telemetryContext.SessionId, remoteContext.SessionId);
        Assert.Equal($"{TelemetryApps.FoundryOsd}@1.2.3", remoteContext.Release);
    }

    [Fact]
    public void RemoteDiagnosticsLifecycle_Initialize_WhenConsentIsDisabled_DoesNotRegisterService()
    {
        RemoteDiagnosticsSink.Clear();
        var service = new RecordingRemoteDiagnosticsService();
        TelemetryContext telemetryContext = TelemetryContextFactory.Create(
            TelemetryApps.FoundryOsd,
            "1.2.3",
            "debug",
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            TelemetryBootMediaTargets.Usb,
            "x64",
            "en-US");

        try
        {
            RemoteDiagnosticsLifecycle.Initialize(
                service,
                new TelemetrySettings
                {
                    IsEnabled = true,
                    IsRemoteDiagnosticsEnabled = false,
                    InstallId = "install-id",
                    HostUrl = TelemetryDefaults.PostHogEuHost,
                    ProjectToken = "project-token"
                },
                telemetryContext);

            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Failed"));

            Assert.Equal(0, service.ConfigureCallCount);
            Assert.Equal(1, service.DisableCallCount);
            Assert.Equal(0, service.EmitCallCount);
        }
        finally
        {
            RemoteDiagnosticsSink.Clear();
        }
    }

    [Fact]
    public void RemoteDiagnosticsLifecycle_Initialize_WhenConsentChanges_UpdatesLiveRegistration()
    {
        RemoteDiagnosticsSink.Clear();
        var service = new RecordingRemoteDiagnosticsService();
        TelemetryContext telemetryContext = TelemetryContextFactory.Create(
            TelemetryApps.FoundryOsd,
            "1.2.3",
            "debug",
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            TelemetryBootMediaTargets.Usb,
            "x64",
            "en-US");
        var settings = new TelemetrySettings
        {
            IsRemoteDiagnosticsEnabled = true,
            InstallId = "install-id",
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = "project-token"
        };

        try
        {
            RemoteDiagnosticsLifecycle.Initialize(service, settings, telemetryContext);
            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Enabled"));

            RemoteDiagnosticsLifecycle.Initialize(
                service,
                settings with { IsRemoteDiagnosticsEnabled = false },
                telemetryContext);
            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Disabled"));

            RemoteDiagnosticsLifecycle.Initialize(service, settings, telemetryContext);
            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Re-enabled"));

            Assert.Equal(2, service.ConfigureCallCount);
            Assert.Equal(1, service.DisableCallCount);
            Assert.Equal(2, service.EmitCallCount);
        }
        finally
        {
            RemoteDiagnosticsSink.Clear();
        }
    }

    [Fact]
    public async Task RemoteDiagnosticsLifecycle_ShutdownAsync_WhenFlushIsCancelled_ClearsSinkAndDisposesService()
    {
        RemoteDiagnosticsSink.Clear();
        var service = new BlockingRemoteDiagnosticsService();
        TelemetryContext telemetryContext = TelemetryContextFactory.Create(
            TelemetryApps.FoundryOsd,
            "1.2.3",
            "debug",
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            TelemetryBootMediaTargets.Usb,
            "x64",
            "en-US");

        RemoteDiagnosticsLifecycle.Initialize(
            service,
            new TelemetrySettings
            {
                IsEnabled = true,
                IsRemoteDiagnosticsEnabled = true,
                InstallId = "install-id",
                HostUrl = TelemetryDefaults.PostHogEuHost,
                ProjectToken = "project-token"
            },
            telemetryContext);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RemoteDiagnosticsLifecycle.ShutdownAsync(service, cancellation.Token));

        RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Failed"));

        Assert.Equal(1, service.ConfigureCallCount);
        Assert.Equal(1, service.DisposeCallCount);
        Assert.Equal(0, service.EmitCallCount);
    }

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
                ["boot_media_usb_operation"] = "none",
                ["boot_media_architecture"] = "arm64",
                ["boot_media_creation_failed_step_name"] = "Customize boot image",
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
        Assert.Equal("none", properties.GetProperty("boot_media_usb_operation").GetString());
        Assert.Equal("arm64", properties.GetProperty("boot_media_architecture").GetString());
        Assert.Equal("Customize boot image", properties.GetProperty("boot_media_creation_failed_step_name").GetString());
        Assert.False(properties.TryGetProperty("failed_step_name", out _));
        Assert.False(properties.TryGetProperty("ssid", out _));
        Assert.Equal(TelemetryApps.FoundryOsd, properties.GetProperty("app").GetString());
        Assert.Equal("1.2.3", properties.GetProperty("app_version").GetString());
        Assert.Equal(TelemetryRuntimeModes.Desktop, properties.GetProperty("app_runtime").GetString());
        Assert.Equal("x64", properties.GetProperty("app_runtime_architecture").GetString());
        Assert.Equal("en-US", properties.GetProperty("app_locale").GetString());
        Assert.Equal(TelemetryDefaults.SchemaVersion, properties.GetProperty("telemetry_schema_version").GetInt32());
        Assert.False(properties.TryGetProperty("runtime", out _));
        Assert.False(properties.TryGetProperty("runtime_payload_source", out _));
        Assert.False(properties.TryGetProperty("runtime_architecture", out _));
        Assert.False(properties.TryGetProperty("locale", out _));
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
    public async Task TrackAsync_ForConnectSessionReady_AddsEventSpecificRuntimeContext()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        await service.TrackAsync(
            TelemetryEvents.ConnectSessionReady,
            new Dictionary<string, object?>
            {
                ["boot_media_target"] = "unknown",
                ["connect_runtime_payload_source"] = "unknown"
            });

        JsonElement properties = handler.ReadJson().GetProperty("properties");
        Assert.Equal(TelemetryBootMediaTargets.Usb, properties.GetProperty("boot_media_target").GetString());
        Assert.Equal(TelemetryRuntimePayloadSources.None, properties.GetProperty("connect_runtime_payload_source").GetString());
        Assert.False(properties.TryGetProperty("deploy_runtime_payload_source", out _));
        Assert.False(properties.TryGetProperty("runtime_payload_source", out _));
    }

    [Fact]
    public async Task TrackAsync_ForDeploySessionFinished_AddsEventSpecificRuntimeContext()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        await service.TrackAsync(
            TelemetryEvents.DeploySessionFinished,
            new Dictionary<string, object?>
            {
                ["boot_media_target"] = "unknown",
                ["deploy_runtime_payload_source"] = "unknown"
            });

        JsonElement properties = handler.ReadJson().GetProperty("properties");
        Assert.Equal(TelemetryBootMediaTargets.Usb, properties.GetProperty("boot_media_target").GetString());
        Assert.Equal(TelemetryRuntimePayloadSources.None, properties.GetProperty("deploy_runtime_payload_source").GetString());
        Assert.False(properties.TryGetProperty("connect_runtime_payload_source", out _));
        Assert.False(properties.TryGetProperty("runtime_payload_source", out _));
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

    private class RecordingRemoteDiagnosticsService : IRemoteDiagnosticsService
    {
        public int ConfigureCallCount { get; private set; }

        public int EmitCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public int DisableCallCount { get; private set; }

        public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
        {
            ConfigureCallCount++;
        }

        public void Emit(LogEvent logEvent)
        {
            EmitCallCount++;
        }

        public void Disable()
        {
            DisableCallCount++;
        }

        public virtual Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRemoteDiagnosticsService : RecordingRemoteDiagnosticsService
    {
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
