// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Bootstrap.SystemPreparation;
using Foundry.Core.Models.Configuration.Deploy;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Foundry.Bootstrap.Tests.SystemPreparation;

public sealed class WinPeSystemPreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FoundryBootstrap-{Guid.NewGuid():N}");

    public WinPeSystemPreparationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Config"));
    }

    [Fact]
    public async Task PrepareSystemAsync_UsesEnvironmentTimeZoneBeforeDeployConfiguration()
    {
        File.WriteAllText(
            Path.Combine(_root, "Config", "foundry.deploy.config.json"),
            """{"localization":{"defaultTimeZoneId":"UTC"}}""");
        var platform = new RecordingPlatform
        {
            EnvironmentTimeZoneId = "Romance Standard Time",
            ValidTimeZoneIds = ["Romance Standard Time", "UTC"]
        };
        var preparation = CreatePreparation(platform);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("Romance Standard Time", Assert.Single(platform.AppliedTimeZoneIds));
    }

    [Fact]
    public async Task PrepareSystemAsync_UsesDeployConfigurationBeforePublicIpLookup()
    {
        File.WriteAllText(
            Path.Combine(_root, "Config", "foundry.deploy.config.json"),
            """{"localization":{"defaultTimeZoneId":"Central Standard Time"}}""");
        var platform = new RecordingPlatform
        {
            ValidTimeZoneIds = ["Central Standard Time", "UTC"]
        };
        var handler = new StubHttpHandler(request => throw new InvalidOperationException($"Unexpected request to {request.RequestUri?.Host}."));
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("Central Standard Time", Assert.Single(platform.AppliedTimeZoneIds));
        Assert.Equal(2, handler.RequestCount); // Clock probes only; timezone providers are skipped.
    }

    [Theory]
    [InlineData(null, "UTC", false)]
    [InlineData("Central Standard Time", "Central Standard Time", false)]
    [InlineData("Central Standard Time", "Central Standard Time", true)]
    public async Task PrepareSystemAsync_ReadsSerializedDeployConfigurationWithOptionalTimeZone(string? configuredTimeZone, string expectedTimeZone, bool pascalCase)
    {
        var configuration = new FoundryDeployConfigurationDocument
        {
            Localization = new DeployLocalizationSettings { DefaultTimeZoneId = configuredTimeZone }
        };
        string json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
        {
            PropertyNamingPolicy = pascalCase ? null : JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(Path.Combine(_root, "Config", "foundry.deploy.config.json"), json);
        var warnings = new List<string>();
        var platform = new RecordingPlatform { ValidTimeZoneIds = ["UTC", "Central Standard Time"] };
        var preparation = CreatePreparation(platform, warningCallback: warnings.Add);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal(expectedTimeZone, Assert.Single(platform.AppliedTimeZoneIds));
        Assert.DoesNotContain(warnings, warning => warning.StartsWith("Embedded deployment timezone", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"localization\":null}", false)]
    [InlineData("{\"localization\":{\"defaultTimeZoneId\":null}}", false)]
    [InlineData("{\"Localization\":{/* optional */ \"DefaultTimeZoneId\":\"UTC\",},}", false)]
    [InlineData("{", true)]
    [InlineData("{\"localization\":42}", true)]
    [InlineData("{\"localization\":{\"defaultTimeZoneId\":42}}", true)]
    public async Task PrepareSystemAsync_WarnsOnlyForMalformedTimeZoneConfiguration(string json, bool expectedWarning)
    {
        File.WriteAllText(Path.Combine(_root, "Config", "foundry.deploy.config.json"), json);
        var warnings = new List<string>();
        var platform = new RecordingPlatform { ValidTimeZoneIds = ["UTC"] };
        var preparation = CreatePreparation(platform, warningCallback: warnings.Add);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("UTC", Assert.Single(platform.AppliedTimeZoneIds));
        Assert.Equal(expectedWarning, warnings.Any(warning => warning.StartsWith("Embedded deployment timezone", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PrepareSystemAsync_MapsPublicIpIanaTimeZoneWhenNoOverrideIsConfigured()
    {
        File.WriteAllText(
            Path.Combine(_root, "Config", "iana-windows-timezones.json"),
            """{"Europe/Paris":"Romance Standard Time"}""");
        var platform = new RecordingPlatform
        {
            ValidTimeZoneIds = ["Romance Standard Time", "UTC"]
        };
        var handler = new StubHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"timezone\":\"Europe/Paris\"}", Encoding.UTF8, "application/json")
            };
        });
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("Romance Standard Time", Assert.Single(platform.AppliedTimeZoneIds));
    }

    [Fact]
    public async Task PrepareSystemAsync_FallsBackToUtcWhenAutomaticDetectionFails()
    {
        var platform = new RecordingPlatform { ValidTimeZoneIds = ["UTC"] };
        var preparation = CreatePreparation(platform);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("UTC", Assert.Single(platform.AppliedTimeZoneIds));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(-36000, true)]
    [InlineData(36000, true)]
    public async Task PrepareSystemAsync_UpdatesClockAboveOneSecondSkew(int skewSeconds, bool expectedUpdate)
    {
        DateTimeOffset now = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var platform = new RecordingPlatform { UtcNow = now, ValidTimeZoneIds = ["UTC"] };
        var handler = new StubHttpHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Date = now.AddSeconds(skewSeconds);
            return response;
        });
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal(expectedUpdate ? 1 : 0, platform.AppliedUtcTimes.Count);
        Assert.True(preparation.IsClockUsable);
    }

    [Fact]
    public async Task PrepareSystemAsync_IgnoresDateHeaderFromFailedClockProbe()
    {
        DateTimeOffset now = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var platform = new RecordingPlatform { UtcNow = now, ValidTimeZoneIds = ["UTC"] };
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.Date = now.AddHours(1);
            return response;
        });
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Empty(platform.AppliedUtcTimes);
        Assert.False(preparation.IsClockUsable);
    }

    [Fact]
    public async Task PreparationFailuresAreWarningsAndDoNotStopRemainingWork()
    {
        var sink = new CollectingSink();
        var platform = new RecordingPlatform
        {
            ServiceException = new InvalidOperationException("service failure"),
            TimeZoneException = new InvalidOperationException("timezone failure"),
            ValidTimeZoneIds = ["UTC"]
        };
        var preparation = CreatePreparation(platform, logger: new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());

        await preparation.PrepareNetworkAsync(CancellationToken.None);
        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Contains(sink.Events, entry => entry.Level == LogEventLevel.Warning);
        Assert.Contains("UTC", platform.AttemptedTimeZoneIds);
    }

    [Fact]
    public async Task RecoverableFailureInvokesWarningCallbackWithSafeMessage()
    {
        var warnings = new List<string>();
        var platform = new RecordingPlatform { ServiceException = new TimeoutException("sensitive detail") };
        var preparation = CreatePreparation(platform, warningCallback: warnings.Add);

        await preparation.PrepareNetworkAsync(CancellationToken.None);

        Assert.Contains("Wired AutoConfig service could not be started. Boot will continue.", warnings);
        Assert.DoesNotContain(warnings, warning => warning.Contains("sensitive detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareSystemAsync_TimesOutWhenTimeZoneResponseBodyStalls()
    {
        var warnings = new List<string>();
        var platform = new RecordingPlatform { ValidTimeZoneIds = ["UTC"] };
        var handler = new StubHttpHandler(request => request.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
        var preparation = CreatePreparation(platform, handler, warningCallback: warnings.Add);

        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal("UTC", Assert.Single(platform.AppliedTimeZoneIds));
        Assert.Contains("Public IP timezone could not be resolved. Boot will continue with UTC.", warnings);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preparation = CreatePreparation(new RecordingPlatform { ValidTimeZoneIds = ["UTC"] });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preparation.PrepareNetworkAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preparation.PrepareSystemAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task PrepareNetworkAsync_StartsWlanOnlyInWinPeWithRequiredDependencies(
        bool isWinPe,
        bool dependenciesPresent,
        bool expectedWlanStart)
    {
        var platform = new RecordingPlatform
        {
            IsWinPeSystemDrive = isWinPe,
            WirelessDependenciesPresent = dependenciesPresent
        };
        var preparation = CreatePreparation(platform);

        await preparation.PrepareNetworkAsync(CancellationToken.None);

        Assert.Contains("dot3svc", platform.StartedServices);
        Assert.Equal(expectedWlanStart, platform.StartedServices.Contains("WlanSvc"));
    }

    [Fact]
    public async Task PrepareClockAsync_RetriesAfterConnectWhenInitiallyOffline()
    {
        var platform = new RecordingPlatform { ValidTimeZoneIds = ["UTC"] };
        bool online = false;
        var handler = new StubHttpHandler(_ =>
        {
            if (!online) throw new HttpRequestException("Offline");
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Date = platform.UtcNow.AddHours(-10);
            return response;
        });
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareClockAsync(CancellationToken.None);
        Assert.False(preparation.IsClockUsable);
        Assert.Empty(platform.AppliedTimeZoneIds);
        online = true;
        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.True(preparation.IsClockUsable);
        Assert.Single(platform.AppliedUtcTimes);
    }

    [Fact]
    public async Task PrepareClockAsync_DoesNotRepeatSuccessfulProbeOrConfigureTimeZoneEarly()
    {
        var platform = new RecordingPlatform { EnvironmentTimeZoneId = "UTC", ValidTimeZoneIds = ["UTC"] };
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Date = platform.UtcNow;
            return response;
        });
        var preparation = CreatePreparation(platform, handler);

        await preparation.PrepareClockAsync(CancellationToken.None);
        Assert.True(preparation.IsClockUsable);
        Assert.Empty(platform.AppliedTimeZoneIds);
        await preparation.PrepareSystemAsync(CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Single(platform.AppliedTimeZoneIds);
    }

    [Fact]
    public async Task PrepareClockAsync_ContinuesAfterItsBudgetButPropagatesOperatorCancellation()
    {
        using var httpClient = new HttpClient(new StalledHttpHandler());
        var preparation = new WinPeSystemPreparation(_root, httpClient, Logger.None,
            new RecordingPlatform(), TimeSpan.FromSeconds(30));
        await preparation.PrepareClockAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(preparation.IsClockUsable);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation.PrepareClockAsync(cancellation.Token));
    }

    private sealed class StalledHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request must be cancelled.");
        }
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private WinPeSystemPreparation CreatePreparation(
        RecordingPlatform platform,
        HttpMessageHandler? handler = null,
        ILogger? logger = null,
        Action<string>? warningCallback = null)
    {
        var httpClient = new HttpClient(handler ?? new StubHttpHandler(_ => throw new HttpRequestException("Offline.")));
        return new WinPeSystemPreparation(
            _root,
            httpClient,
            logger ?? Logger.None,
            platform,
            TimeSpan.FromMilliseconds(100),
            warningCallback);
    }

    private sealed class RecordingPlatform : ISystemPreparationPlatform
    {
        public string? EnvironmentTimeZoneId { get; init; }
        public bool IsWinPeSystemDrive { get; init; } = true;
        public bool WirelessDependenciesPresent { get; init; } = true;
        public DateTimeOffset UtcNow { get; init; } = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        public HashSet<string> ValidTimeZoneIds { get; init; } = [];
        public Exception? ServiceException { get; init; }
        public Exception? TimeZoneException { get; init; }
        public List<string> StartedServices { get; } = [];
        public List<string> AttemptedTimeZoneIds { get; } = [];
        public List<string> AppliedTimeZoneIds { get; } = [];
        public List<DateTimeOffset> AppliedUtcTimes { get; } = [];

        public Task EnsureServiceRunningAsync(string serviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedServices.Add(serviceName);
            return ServiceException is null ? Task.CompletedTask : Task.FromException(ServiceException);
        }

        public Task SetSystemTimeAsync(DateTimeOffset utcTime, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppliedUtcTimes.Add(utcTime);
            return Task.CompletedTask;
        }

        public bool IsValidWindowsTimeZone(string timeZoneId) => ValidTimeZoneIds.Contains(timeZoneId);

        public string? GetCurrentTimeZoneId() => null;

        public Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptedTimeZoneIds.Add(timeZoneId);
            if (TimeZoneException is not null)
            {
                return Task.FromException(TimeZoneException);
            }

            AppliedTimeZoneIds.Add(timeZoneId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
