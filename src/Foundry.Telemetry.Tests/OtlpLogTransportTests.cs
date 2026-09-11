// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class OtlpLogTransportTests
{
    [Theory]
    [InlineData(-10, false)]
    [InlineData(10, false)]
    [InlineData(-10, true)]
    [InlineData(10, true)]
    public async Task SendAsync_UsesIngestionTimeForUnsynchronizedEventsIncludingReplay(int skewHours, bool replay)
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var transport = CreateTransport(handler);
        RemoteDiagnosticRecord record = CreateRecord() with
        {
            Timestamp = DateTimeOffset.UtcNow.AddHours(skewHours),
            Attributes = new Dictionary<string, object> { ["diagnostics.clock_synchronized"] = false }
        };
        if (replay) record = JsonSerializer.Deserialize<RemoteDiagnosticRecord>(JsonSerializer.Serialize(record))!;

        await transport.SendAsync([record], CancellationToken.None);

        byte[] resource = Assert.Single(ReadMessages(handler.Body, 1));
        byte[] scope = Assert.Single(ReadMessages(resource, 2));
        byte[] log = Assert.Single(ReadMessages(scope, 2));
        Assert.Equal(0UL, ReadNumber(log, 1, defaultValue: 0));
        Dictionary<string, byte[]> attributes = ReadAttributes(log, 6);
        Assert.Equal(record.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ReadString(attributes["diagnostics.original_timestamp"], 1));
        Assert.Equal("ingestion", ReadString(attributes["diagnostics.timestamp_source"], 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_PreservesAllSeveritiesOriginalTimeStructuredValuesAndLiteralBody(bool replayFromJson)
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var transport = CreateTransport(handler);
        RemoteDiagnosticRecord[] records = Enum.GetValues<LogEventLevel>()
            .Select(level => CreateRecord(level)).ToArray();
        if (replayFromJson)
        {
            records = JsonSerializer.Deserialize<RemoteDiagnosticRecord[]>(JsonSerializer.Serialize(records))!;
        }

        LogBatchResult result = await transport.SendAsync(records, CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Accepted, result.Disposition);
        Assert.Equal(new Uri("https://eu.i.posthog.com/i/v1/logs"), handler.RequestUri);
        Assert.Equal("Bearer phc_test", handler.Authorization);
        Assert.Equal("application/x-protobuf", handler.ContentType);
        List<byte[]> resourceLogs = ReadMessages(handler.Body, 1);
        Assert.Equal(6, resourceLogs.Count);
        List<byte[]> logs = resourceLogs.SelectMany(resource => ReadMessages(resource, 2))
            .SelectMany(scope => ReadMessages(scope, 2)).ToList();
        Assert.Equal(new ulong[] { 1, 5, 9, 13, 17, 21 }, logs.Select(log => ReadNumber(log, 2)));
        foreach (byte[] log in logs)
        {
            Assert.Equal(1_700_000_000_123_456_700UL, ReadNumber(log, 1));
            Assert.Equal("Driver {original} C:\\Drivers\\net.inf", ReadString(Assert.Single(ReadMessages(log, 5)), 1));
            Dictionary<string, byte[]> attributes = ReadAttributes(log, 6);
            Assert.Equal("Session with full context", ReadString(attributes["session.id"], 1));
            Assert.Equal("System.InvalidOperationException", ReadString(attributes["exception.type"], 1));
            Assert.Equal("at Install() in C:\\Source\\Install.cs:line 42", ReadString(attributes["exception.stacktrace"], 1));
            byte[] nestedMap = Assert.Single(ReadMessages(attributes["details"], 6));
            Dictionary<string, byte[]> nested = ReadAttributes(nestedMap, 1);
            Assert.Equal(12UL, ReadNumber(nested["count"], 3));
            Assert.Equal("net.inf", ReadString(nested["file"], 1));
            Assert.Equal("empty key", ReadString(nested[""], 1));
            byte[] array = Assert.Single(ReadMessages(attributes["codes"], 5));
            Assert.Equal(new ulong[] { 3, 7 }, ReadMessages(array, 1).Select(value => ReadNumber(value, 3)));
        }
        Dictionary<string, byte[]> resourceAttributes = ReadAttributes(Assert.Single(ReadMessages(resourceLogs[0], 1)), 1);
        Assert.Equal("foundry.bootstrap", ReadString(resourceAttributes["service.name"], 1));
        Assert.Equal("old-version", ReadString(resourceAttributes["service.version"], 1));
    }

    [Theory]
    [InlineData(429, (int)LogBatchDisposition.Retry)]
    [InlineData(500, (int)LogBatchDisposition.Retry)]
    [InlineData(503, (int)LogBatchDisposition.Retry)]
    [InlineData(408, (int)LogBatchDisposition.Retry)]
    [InlineData(400, (int)LogBatchDisposition.Rejected)]
    [InlineData(401, (int)LogBatchDisposition.Rejected)]
    [InlineData(403, (int)LogBatchDisposition.Rejected)]
    [InlineData(302, (int)LogBatchDisposition.Rejected)]
    public async Task SendAsync_ClassifiesHttpFailuresAndHonorsRetryAfter(int status, int expectedValue)
    {
        using var handler = new RecordingHandler((HttpStatusCode)status, retryAfter: TimeSpan.FromSeconds(19));
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord()], CancellationToken.None);

        var expected = (LogBatchDisposition)expectedValue;
        Assert.Equal(expected, result.Disposition);
        if (expected == LogBatchDisposition.Retry)
        {
            Assert.Equal(TimeSpan.FromSeconds(19), result.RetryAfter);
        }
        else
        {
            Assert.Equal(1, result.RejectedRecordCount);
        }
    }

    [Fact]
    public async Task SendAsync_PartialSuccessIsTerminalAndReportsOnlyRejectedCount()
    {
        // ExportLogsServiceResponse.partial_success.rejected_log_records = 1.
        using var handler = new RecordingHandler(HttpStatusCode.OK, [0x0a, 0x02, 0x08, 0x01]);
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord(), CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Rejected, result.Disposition);
        Assert.Equal(1, result.RejectedRecordCount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_PartialSuccessWarningWithoutRejectionIsAccepted()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, [0x0a, 0x03, 0x12, 0x01, 0x78]);
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Accepted, result.Disposition);
    }

    [Theory]
    [InlineData(" { }\n", 0)]
    [InlineData("{\"partialSuccess\":{\"rejectedLogRecords\":\"1\",\"errorMessage\":\"invalid row\"}}", 1)]
    [InlineData("{\"partial_success\":{\"rejected_log_records\":1}}", 1)]
    [InlineData("{\"partialSuccess\":{\"errorMessage\":\"warning only\"}}", 0)]
    public async Task SendAsync_HandlesPostHogJsonAcknowledgments(string response, int rejected)
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, Encoding.UTF8.GetBytes(response), responseContentType: "application/json");
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord(), CreateRecord()], CancellationToken.None);

        Assert.Equal(rejected == 0 ? LogBatchDisposition.Accepted : LogBatchDisposition.Rejected, result.Disposition);
        Assert.Equal(rejected, result.RejectedRecordCount);
    }

    [Fact]
    public async Task SendAsync_OversizedBatchRequestsSplitWithoutAcknowledgingRecords()
    {
        using var handler = new RecordingHandler(HttpStatusCode.RequestEntityTooLarge);
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord(), CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Split, result.Disposition);
        Assert.Equal(0, result.RejectedRecordCount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_OversizedSingleRecordIsExplicitlyRejected()
    {
        using var handler = new RecordingHandler(HttpStatusCode.RequestEntityTooLarge);
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Rejected, result.Disposition);
        Assert.Equal(1, result.RejectedRecordCount);
    }

    [Fact]
    public async Task SendAsync_MalformedSuccessResponseIsNotAcknowledged()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, [0x0a, 0xff]);
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Retry, result.Disposition);
    }

    [Fact]
    public async Task SendAsync_NetworkFailureRetainsBatchForRetry()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, failure: new HttpRequestException("offline"));
        using var transport = CreateTransport(handler);

        LogBatchResult result = await transport.SendAsync([CreateRecord()], CancellationToken.None);

        Assert.Equal(LogBatchDisposition.Retry, result.Disposition);
    }

    [Fact]
    public async Task SendAsync_CancellationIsPropagatedWithoutAcknowledgment()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var transport = CreateTransport(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync([CreateRecord()], cancellation.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotTruncateLongBodyOrExceptionStack()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var transport = CreateTransport(handler);
        string body = new('b', 30_000);
        string stack = new('s', 40_000);
        RemoteDiagnosticRecord record = CreateRecord() with
        {
            Body = body,
            Exception = new RemoteDiagnosticException("Exception", "details", stack, [])
        };

        await transport.SendAsync([record], CancellationToken.None);

        byte[] resource = Assert.Single(ReadMessages(handler.Body, 1));
        byte[] scope = Assert.Single(ReadMessages(resource, 2));
        byte[] log = Assert.Single(ReadMessages(scope, 2));
        Assert.Equal(body, ReadString(Assert.Single(ReadMessages(log, 5)), 1));
        Assert.Equal(stack, ReadString(ReadAttributes(log, 6)["exception.stacktrace"], 1));
    }

    private static OtlpLogTransport CreateTransport(HttpMessageHandler handler) => new(
        RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context(), handler);

    private static RemoteDiagnosticRecord CreateRecord(LogEventLevel level = LogEventLevel.Information) => new(
        DateTimeOffset.UnixEpoch.AddTicks(17_000_000_001_234_567), level,
        "Driver {original} C:\\Drivers\\net.inf",
        new Dictionary<string, object>
        {
            ["service.name"] = "foundry.bootstrap",
            ["service.version"] = "old-version",
            ["session.id"] = "Session with full context",
            ["details"] = new Dictionary<string, object> { ["count"] = 12, ["file"] = "net.inf", [""] = "empty key" },
            ["codes"] = new[] { 3, 7 }
        },
        new RemoteDiagnosticException("System.InvalidOperationException", "failure",
            "at Install() in C:\\Source\\Install.cs:line 42", []));

    private static Dictionary<string, byte[]> ReadAttributes(byte[] data, int field) =>
        ReadMessages(data, field).ToDictionary(attribute => ReadString(attribute, 1), attribute => Assert.Single(ReadMessages(attribute, 2)));

    private static string ReadString(byte[] data, int field)
    {
        List<byte[]> values = ReadMessages(data, field);
        return values.Count == 0 ? string.Empty : Encoding.UTF8.GetString(Assert.Single(values));
    }

    private static List<byte[]> ReadMessages(byte[] data, int field)
    {
        var result = new List<byte[]>();
        using var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == (uint)((field << 3) | 2))
            {
                result.Add(input.ReadBytes().ToByteArray());
            }
            else
            {
                input.SkipLastField();
            }
        }
        return result;
    }

    private static ulong ReadNumber(byte[] data, int field, ulong? defaultValue = null)
    {
        using var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == (uint)(field << 3))
            {
                return input.ReadUInt64();
            }
            if (tag == (uint)((field << 3) | 1))
            {
                return input.ReadFixed64();
            }
            input.SkipLastField();
        }
        return defaultValue ?? throw new InvalidDataException($"Missing numeric field {field}.");
    }

    private sealed class RecordingHandler(HttpStatusCode status, byte[]? response = null, TimeSpan? retryAfter = null,
        Exception? failure = null, string responseContentType = "application/x-protobuf") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? ContentType { get; private set; }
        public byte[] Body { get; private set; } = [];
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            if (failure is not null)
            {
                throw failure;
            }
            var message = new HttpResponseMessage(status) { Content = new ByteArrayContent(response ?? []) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(responseContentType);
            if (retryAfter is not null)
            {
                message.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
            }
            return message;
        }
    }
}
