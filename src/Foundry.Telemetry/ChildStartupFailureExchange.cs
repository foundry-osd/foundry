// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>Persists a child-owned startup failure locally for exclusive recovery by Bootstrap after child exit.</summary>
public static class ChildStartupFailureExchange
{
    /// <summary>The bounded record resides beside the launch status, never at a path read from its contents.</summary>
    public const string FileName = "startup-failure.json";
    internal const int MaximumBytes = 256 * 1024;
    private static readonly object CaptureGate = new();

    /// <summary>
    /// Writes sanitized evidence atomically before startup_failed is published. Returns its stable ID only after persistence.
    /// Performs no transport or service resolution. Use only during protected startup, not an unhandled terminating crash.
    /// </summary>
    public static Guid? TryCapture(string launchDirectory, Guid launchId, RemoteDiagnosticsOptions options,
        RemoteDiagnosticsContext context, LogEvent logEvent)
    {
        lock (CaptureGate) return CaptureCore(launchDirectory, launchId, options, context, logEvent);
    }

    private static Guid? CaptureCore(string launchDirectory, Guid launchId, RemoteDiagnosticsOptions options,
        RemoteDiagnosticsContext context, LogEvent logEvent)
    {
        if (!options.CanSend || launchId == Guid.Empty ||
            context.App is not (TelemetryApps.FoundryConnect or TelemetryApps.FoundryDeploy) || logEvent.Level < LogEventLevel.Error)
            return null;
        try
        {
            string path = ResolvePath(launchDirectory);
            string scope = BootstrapTelemetryJournal.Scope(options.HostUrl, options.ProjectToken, options.InstallId);
            ChildStartupFailureRecord? previous = Read(launchDirectory, launchId, context.App);
            if (previous is not null && previous.Scope == scope) return previous.RecordId;
            Guid id = Guid.NewGuid();
            var record = new ChildStartupFailureRecord(1, launchId, id, logEvent.Exception is null ? id : Guid.NewGuid(), scope,
                RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(logEvent, context));
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            if (bytes.Length > MaximumBytes) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ResolvePath(launchDirectory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                ResolvePath(launchDirectory);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return record.RecordId;
        }
#pragma warning disable CA1031 // Failure evidence is best effort and cannot replace the original startup failure.
        catch (Exception) { return null; }
#pragma warning restore CA1031
    }

    internal static ChildStartupFailureRecord? Read(string launchDirectory, Guid launchId, string application)
    {
        try
        {
            string path = ResolvePath(launchDirectory);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) return null;
            ChildStartupFailureRecord? record = JsonSerializer.Deserialize<ChildStartupFailureRecord>(stream);
            if (record is not { Version: 1, Diagnostic: { Attributes: not null, Body: not null } } ||
                record.LaunchId != launchId || record.RecordId == Guid.Empty || record.LogRecordId == Guid.Empty ||
                (record.LogRecordId == record.RecordId && record.Diagnostic.Exception is not null) || record.Scope is not { Length: 64 } ||
                record.Diagnostic.Level is not (LogEventLevel.Error or LogEventLevel.Fatal)) return null;
            RemoteDiagnosticRecord diagnostic = BootstrapTelemetryJournal.RevalidateDiagnostic(record.Diagnostic);
            if (application is not (TelemetryApps.FoundryConnect or TelemetryApps.FoundryDeploy) ||
                !diagnostic.Attributes.TryGetValue("service.name", out object? app) || !Equals(app, application)) return null;
            return record with { Diagnostic = diagnostic };
        }
#pragma warning disable CA1031 // Corrupt or inaccessible exchange files are ignored without affecting startup.
        catch (Exception) { return null; }
#pragma warning restore CA1031
    }

    private static string ResolvePath(string launchDirectory)
    {
        if (!Path.IsPathFullyQualified(launchDirectory))
            throw new ArgumentException("Failure exchange paths must be absolute local paths.", nameof(launchDirectory));
        string path = Path.Combine(Path.GetFullPath(launchDirectory), FileName);
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Failure exchange paths must be local.", nameof(launchDirectory));
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Failure exchange paths must not contain links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return path;
    }

    internal static void Retire(string launchDirectory)
    {
        try { File.Delete(ResolvePath(launchDirectory)); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
}

/// <summary>Credentials are replaced by a destination/installation digest; IDs survive transfer and replay.</summary>
internal sealed record ChildStartupFailureRecord(int Version, Guid LaunchId, Guid RecordId, Guid LogRecordId,
    string Scope, RemoteDiagnosticRecord Diagnostic);
