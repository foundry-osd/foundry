// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Serilog;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>
/// Applies best-effort network, clock, and timezone preparation for WinPE.
/// </summary>
public sealed class WinPeSystemPreparation : ISystemPreparation
{
    private static readonly TimeSpan ClockSkewThreshold = TimeSpan.FromMinutes(5);
    private static readonly Uri[] ClockProbeUris =
    [
        new("http://www.msftconnecttest.com/connecttest.txt"),
        new("http://www.google.com")
    ];
    private static readonly TimeZoneProvider[] TimeZoneProviders =
    [
        new("time.now", new Uri("https://time.now/developer/api/ip"), true),
        new("ipapi.co", new Uri("https://ipapi.co/timezone/"), false),
        new("geojs", new Uri("https://get.geojs.io/v1/ip/geo.json"), true)
    ];

    private readonly string _deployConfigurationPath;
    private readonly string _timeZoneMapPath;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly ISystemPreparationPlatform _platform;
    private readonly TimeSpan _requestTimeout;
    private readonly Action<string>? _warningCallback;

    public WinPeSystemPreparation(
        string winPeRoot,
        HttpClient httpClient,
        ILogger logger,
        Action<string>? warningCallback = null)
        : this(
            winPeRoot,
            httpClient,
            logger,
            new WindowsSystemPreparationPlatform(winPeRoot),
            TimeSpan.FromSeconds(10),
            warningCallback)
    {
    }

    internal WinPeSystemPreparation(
        string winPeRoot,
        HttpClient httpClient,
        ILogger logger,
        ISystemPreparationPlatform platform,
        TimeSpan requestTimeout,
        Action<string>? warningCallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(winPeRoot);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(platform);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _deployConfigurationPath = Path.Combine(winPeRoot, "Config", "foundry.deploy.config.json");
        _timeZoneMapPath = Path.Combine(winPeRoot, "Config", "iana-windows-timezones.json");
        _httpClient = httpClient;
        _logger = logger.ForContext<WinPeSystemPreparation>();
        _platform = platform;
        _requestTimeout = requestTimeout;
        _warningCallback = warningCallback;
    }

    public async Task PrepareNetworkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await TryActionAsync(
            "Wired AutoConfig service could not be started. Boot will continue.",
            token => _platform.EnsureServiceRunningAsync("dot3svc", token),
            cancellationToken).ConfigureAwait(false);

        if (!_platform.IsWinPeSystemDrive || !_platform.WirelessDependenciesPresent)
        {
            _logger.Debug("Wi-Fi AutoConfig startup was skipped because WinPE wireless support is unavailable.");
            return;
        }

        await TryActionAsync(
            "Wi-Fi AutoConfig service could not be started. Boot will continue.",
            token => _platform.EnsureServiceRunningAsync("WlanSvc", token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PrepareSystemAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_platform.IsWinPeSystemDrive)
        {
            _logger.Debug("Clock and timezone preparation was skipped outside the WinPE system drive.");
            return;
        }

        await SynchronizeClockAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await ConfigureTimeZoneAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SynchronizeClockAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? internetTime = null;
        foreach (Uri probeUri in ClockProbeUris)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, probeUri);
                using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                internetTime = response.Headers.Date;
                if (internetTime.HasValue)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Debug("Internet clock probe timed out. Provider={Provider}", probeUri.Host);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Debug("Internet clock probe failed. Provider={Provider}", probeUri.Host);
            }
        }

        if (!internetTime.HasValue)
        {
            Warn("Internet clock could not be resolved. Boot will continue without clock correction.");
            return;
        }

        if ((internetTime.Value - _platform.UtcNow).Duration() <= ClockSkewThreshold)
        {
            _logger.Information("System clock is within the allowed internet time threshold.");
            return;
        }

        await TryActionAsync(
            "System clock could not be corrected. Boot will continue.",
            token => _platform.SetSystemTimeAsync(internetTime.Value.ToUniversalTime(), token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ConfigureTimeZoneAsync(CancellationToken cancellationToken)
    {
        string? targetTimeZoneId = ResolveWindowsTimeZoneId(_platform.EnvironmentTimeZoneId);
        string source = "environment override";

        if (targetTimeZoneId is null)
        {
            targetTimeZoneId = ResolveWindowsTimeZoneId(ReadDeployTimeZoneId());
            source = "deployment configuration";
        }

        if (targetTimeZoneId is null)
        {
            string? detectedTimeZoneId = await DetectTimeZoneIdAsync(cancellationToken).ConfigureAwait(false);
            targetTimeZoneId = ResolveWindowsTimeZoneId(detectedTimeZoneId);
            source = "public IP lookup";
        }

        targetTimeZoneId ??= "UTC";
        if (targetTimeZoneId == "UTC")
        {
            source = "bootstrap fallback";
        }

        if (string.Equals(_platform.GetCurrentTimeZoneId(), targetTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Information("WinPE timezone is already configured. TimeZoneId={TimeZoneId}", targetTimeZoneId);
            return;
        }

        _logger.Information("Applying WinPE timezone. TimeZoneId={TimeZoneId} Source={Source}", targetTimeZoneId, source);
        await TryActionAsync(
            "WinPE timezone could not be updated. Boot will continue.",
            token => _platform.SetTimeZoneAsync(targetTimeZoneId, token),
            cancellationToken).ConfigureAwait(false);
    }

    private string? ReadDeployTimeZoneId()
    {
        if (!File.Exists(_deployConfigurationPath))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_deployConfigurationPath));
            return document.RootElement
                .GetProperty("localization")
                .GetProperty("defaultTimeZoneId")
                .GetString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Warn("Embedded deployment timezone configuration could not be read. Boot will continue with automatic detection.");
            return null;
        }
    }

    private async Task<string?> DetectTimeZoneIdAsync(CancellationToken cancellationToken)
    {
        foreach (TimeZoneProvider provider in TimeZoneProviders)
        {
            try
            {
                string content = await ReadBoundedContentAsync(provider.Uri, cancellationToken).ConfigureAwait(false);
                string? candidate = provider.IsJson ? ReadJsonTimeZone(content) : content.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Debug("Public IP timezone lookup timed out. Provider={Provider}", provider.Name);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Debug("Public IP timezone lookup failed. Provider={Provider}", provider.Name);
            }
        }

        Warn("Public IP timezone could not be resolved. Boot will continue with UTC.");
        return null;
    }

    private string? ResolveWindowsTimeZoneId(string? candidate)
    {
        string? normalized = candidate?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (_platform.IsValidWindowsTimeZone(normalized))
        {
            return normalized;
        }

        IReadOnlyDictionary<string, string>? map = ReadTimeZoneMap();
        return map is not null && map.TryGetValue(normalized, out string? windowsId) && _platform.IsValidWindowsTimeZone(windowsId)
            ? windowsId
            : null;
    }

    private IReadOnlyDictionary<string, string>? ReadTimeZoneMap()
    {
        if (!File.Exists(_timeZoneMapPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_timeZoneMapPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Warn("IANA timezone map could not be loaded. Boot will continue with UTC if needed.");
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
    }

    private async Task<string> ReadBoundedContentAsync(Uri uri, CancellationToken cancellationToken)
    {
        const int maximumResponseBytes = 4096;
        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linked.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumResponseBytes)
        {
            throw new InvalidDataException("Timezone provider response exceeded the allowed size.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[512];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maximumResponseBytes)
            {
                throw new InvalidDataException("Timezone provider response exceeded the allowed size.");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    private async Task TryActionAsync(
        string warningMessage,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Warn(warningMessage, exception);
        }
    }

    private void Warn(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.Warning(message);
        }
        else
        {
            _logger.Warning(exception, message);
        }

        try
        {
            _warningCallback?.Invoke(message);
        }
        catch (Exception callbackException)
        {
            _logger.Warning(callbackException, "System preparation warning could not be presented.");
        }
    }

    private static string? ReadJsonTimeZone(string content)
    {
        using JsonDocument document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("timezone", out JsonElement property) ? property.GetString() : null;
    }

    private sealed record TimeZoneProvider(string Name, Uri Uri, bool IsJson);
}
