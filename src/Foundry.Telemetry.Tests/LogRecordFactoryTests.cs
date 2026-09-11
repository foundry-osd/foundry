// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Utilities.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Telemetry.Tests;

public sealed class LogRecordFactoryTests
{
    [Theory]
    [InlineData("Type")]
    [InlineData("Message")]
    [InlineData("InnerExceptions")]
    public void SanitizePersistedRecord_RejectsMalformedExceptionFieldsAsInvalidJson(string field)
    {
        var exception = new RemoteDiagnosticException("System.Exception", "failed", null, []);
        exception = field switch
        {
            "Type" => exception with { Type = null! },
            "Message" => exception with { Message = null! },
            _ => exception with { InnerExceptions = null! }
        };
        var record = new RemoteDiagnosticRecord(DateTimeOffset.UtcNow, LogEventLevel.Error, "failed",
            new Dictionary<string, object>(), exception);

        Assert.Throws<JsonException>(() => LogRecordFactory.SanitizePersistedRecord(record));
    }

    [Fact]
    public void SanitizePersistedRecord_MasksDictionaryKeysWithoutLosingCollidingValues()
    {
        var record = new RemoteDiagnosticRecord(DateTimeOffset.UtcNow, LogEventLevel.Debug, "results",
            new Dictionary<string, object>
            {
                ["Results"] = new Dictionary<string, object>
                {
                    ["https://example.test?sig=first-secret"] = 42,
                    ["https://example.test?sig=second-secret"] = 43
                }
            }, null);

        RemoteDiagnosticRecord result = LogRecordFactory.SanitizePersistedRecord(record);

        string json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("first-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("second-secret", json, StringComparison.Ordinal);
        using JsonDocument nested = JsonDocument.Parse(JsonSerializer.Serialize(result.Attributes["Results"]));
        Assert.Equal([42, 43], nested.RootElement.EnumerateObject().Select(property => property.Value.GetInt32()).ToArray());
    }

    [Fact]
    public void SanitizePersistedRecord_MasksSecretsWithoutLosingJsonStructureOrOriginalIdentity()
    {
        var original = new RemoteDiagnosticRecord(DateTimeOffset.UtcNow, LogEventLevel.Debug,
            "Reading C:\\boot.wim; password=body-secret", new Dictionary<string, object>
            {
                ["diagnostics.record_id"] = "record-42",
                ["Nested"] = new Dictionary<string, object>
                {
                    ["TenantId"] = "tenant-42",
                    ["AccessToken"] = "token-secret",
                    ["Count"] = 42,
                    ["Rows"] = new object?[] { null, "https://example.test?sig=signature-secret&version=42" }
                }
            }, new RemoteDiagnosticException("System.Exception", "password=exception-secret",
                "at Reading C:\\boot.cs:42", []));
        RemoteDiagnosticRecord loaded = JsonSerializer.Deserialize<RemoteDiagnosticRecord>(JsonSerializer.Serialize(original))!;

        RemoteDiagnosticRecord sanitized = LogRecordFactory.SanitizePersistedRecord(loaded);

        string json = JsonSerializer.Serialize(sanitized);
        Assert.DoesNotContain("body-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("signature-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", json, StringComparison.Ordinal);
        Assert.Equal(original.Timestamp, sanitized.Timestamp);
        Assert.Equal("record-42", sanitized.Attributes["diagnostics.record_id"]);
        Assert.Contains("tenant-42", json, StringComparison.Ordinal);
        Assert.Equal("at Reading C:\\boot.cs:42", sanitized.Exception!.StackTrace);
        using JsonDocument nested = JsonDocument.Parse(JsonSerializer.Serialize(sanitized.Attributes["Nested"]));
        Assert.Equal(42, nested.RootElement.GetProperty("Count").GetInt32());
        Assert.Equal(JsonValueKind.Null, nested.RootElement.GetProperty("Rows")[0].ValueKind);
    }

    [Fact]
    public void Create_PreservesFullMessageNestedPropertiesAndOriginalTime()
    {
        string detail = new('x', 9000);
        var time = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.FromHours(2));
        var source = new LogEvent(time, LogEventLevel.Verbose, null,
            new MessageTemplateParser().Parse("Reading {Path}: {Detail}"),
            [new("Path", new ScalarValue(@"C:\Users\alice\boot.wim")), new("Detail", new ScalarValue(detail)),
             new("Nested", new StructureValue([new("TenantId", new ScalarValue("tenant-42")),
                 new("Rows", new SequenceValue([new ScalarValue(42), new ScalarValue(null)])),
                 new("AccessToken", new ScalarValue("never-export"))]))]);

        RemoteDiagnosticRecord record = LogRecordFactory.Create(source, Context);

        Assert.Equal(time, record.Timestamp);
        Assert.Equal(LogEventLevel.Verbose, record.Level);
        Assert.Contains(detail, record.Body, StringComparison.Ordinal);
        Assert.Contains("alice", record.Body, StringComparison.Ordinal);
        Assert.StartsWith(@"Reading C:\Users\alice\boot.wim: ", record.Body, StringComparison.Ordinal);
        Assert.Equal(@"C:\Users\alice\boot.wim", record.Attributes["Path"]);
        Assert.Equal("foundry_deploy", record.Attributes["service.name"]);
        using JsonDocument nested = JsonDocument.Parse(JsonSerializer.Serialize(record.Attributes["Nested"]));
        Assert.Equal("tenant-42", nested.RootElement.GetProperty("TenantId").GetString());
        Assert.Equal(42, nested.RootElement.GetProperty("Rows")[0].GetInt32());
        Assert.Equal(JsonValueKind.Null, nested.RootElement.GetProperty("Rows")[1].ValueKind);
        Assert.Equal("<redacted>", nested.RootElement.GetProperty("AccessToken").GetString());
        Assert.Equal("Reading {Path}: {Detail}", record.Attributes["message_template.text"]);
        Assert.False(record.ShouldTrackException);
    }

    [Fact]
    public void Create_PreservesSafeExceptionStackTypeAndIdentityAcrossRepeatedNormalization()
    {
        Exception exception;
        try
        {
            throw new InvalidOperationException("Reading C:\\boot.wim failed; password=exception-secret");
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }
        LogEvent source = LogEventNormalizer.Normalize(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error,
            exception, new MessageTemplateParser().Parse("Read failed"), []));

        RemoteDiagnosticRecord first = LogRecordFactory.Create(source, Context);
        RemoteDiagnosticRecord second = LogRecordFactory.Create(source, Context);

        Assert.Equal(first.Attributes["diagnostics.record_id"], second.Attributes["diagnostics.record_id"]);
        Assert.Equal(first.Attributes["diagnostics.sequence"], second.Attributes["diagnostics.sequence"]);
        Assert.NotNull(first.Exception);
        Assert.Equal("System.InvalidOperationException", first.Exception.Type);
        Assert.Contains("C:\\boot.wim", first.Exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", first.Exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Create_PreservesSafeExceptionStackTypeAndIdentityAcrossRepeatedNormalization), first.Exception.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictExceptionPolicy_PreservesOriginalTypesAndAggregateChildrenAfterNormalization()
    {
        var exception = new AggregateException("password=root-secret",
            new InvalidOperationException("password=first-secret"), new TimeoutException("password=second-secret"));
        LogEvent source = LogEventNormalizer.Normalize(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error,
            exception, new MessageTemplateParser().Parse("Read failed"), []));

        RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(source, Context);

        Assert.NotNull(record.Exception);
        Assert.Equal("System.AggregateException", record.Exception.Type);
        Assert.Equal(2, record.Exception.InnerExceptions.Count);
        Assert.Equal("System.InvalidOperationException", record.Exception.InnerExceptions[0].Type);
        Assert.Equal("System.TimeoutException", record.Exception.InnerExceptions[1].Type);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(record), StringComparison.Ordinal);
    }

    private static readonly RemoteDiagnosticsContext Context = new("foundry_deploy", "1.2.3", "release", "winpe", "x64", "en-US", "session-1", "foundry_deploy@1.2.3");
}
