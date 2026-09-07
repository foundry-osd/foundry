// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Creates bounded, sanitized diagnostic snapshots and publishes archives atomically.</summary>
public sealed partial class SupportBundleExporter(TimeProvider? timeProvider = null)
{
    private const int MaximumFileBytes = 10 * 1024 * 1024;
    private const long MaximumTotalBytes = 40L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Publishes an archive only after all required sources and metadata are processed.</summary>
    public async Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sources);
        cancellationToken.ThrowIfCancellationRequested();
        string application = SafeMetadata(request.ApplicationName, 128);
        string version = SafeMetadata(request.ApplicationVersion, 128);
        string session = SafeMetadata(request.SessionId, 128);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationDirectoryPath);
        Directory.CreateDirectory(request.DestinationDirectoryPath);
        var timestamp = _timeProvider.GetUtcNow();
        string archivePath = ResolveAvailablePath(request.DestinationDirectoryPath, $"FoundrySupport-{timestamp:yyyyMMddTHHmmssZ}");
        string temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        var included = new List<IncludedSource>();
        var omissions = new List<OmittedSource>();
        long totalBytes = 0;
        if (!Enum.IsDefined(request.PrivacyMode)) { throw new ArgumentException("Unsupported support privacy mode."); }
        if (request.Sources.Count > 32)
        {
            if (request.PrivacyMode == SupportBundlePrivacyMode.Sanitized && request.Sources.Skip(32).Any(static source => source.Required))
            { throw new IOException("Required diagnostic sources exceed the supported source count."); }
            omissions.Add(new(33, "source-33-and-later", "source_count_limit"));
        }
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int index = 0;
                foreach (var source in request.Sources.Take(32))
                {
                    index++;
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        ValidateSource(source, usedNames);
                        SupportBundleSourcePolicy.RejectReparseChain(source.Path);
                        await using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        long remainingBytes = MaximumTotalBytes - totalBytes;
                        long snapshotLength = input.Length;
                        if (snapshotLength > MaximumFileBytes || snapshotLength > remainingBytes) { throw new IOException("source_byte_limit"); }
                        totalBytes += snapshotLength;
                        byte[] snapshot = await ReadBoundedSnapshotAsync(input, remainingBytes, cancellationToken, snapshotLength).ConfigureAwait(false);
                        SupportBundleSourcePolicy.RejectReparseChain(source.Path);
                        string content = DecodeText(snapshot);
                        if (source.Format == SupportBundleSourceFormat.ActionResultJson || request.PrivacyMode == SupportBundlePrivacyMode.Sanitized)
                        {
                            content = source.Format == SupportBundleSourceFormat.ActionResultJson
                                ? ActionResultSanitizer.Sanitize(content)
                                : DiagnosticContentSanitizer.SanitizeMultiline(content, int.MaxValue);
                        }
                        string directory = source.Format == SupportBundleSourceFormat.ActionResultJson ? "results" : "logs";
                        await WriteEntryAsync(archive, $"{directory}/{source.ArchiveName}", content, cancellationToken).ConfigureAwait(false);
                        included.Add(new(index, source.ArchiveName, source.ArchiveName));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or RegexMatchTimeoutException)
                    {
                        if (source.Required && request.PrivacyMode == SupportBundlePrivacyMode.Sanitized)
                        {
                            throw new IOException($"Required diagnostic source {index} could not be safely exported.");
                        }
                        omissions.Add(new(index, $"source-{index}", "source_unavailable_or_unsupported"));
                    }
                }
                if (request.Summary.Count > 32) { throw new IOException("Diagnostic summary exceeds its supported bounds."); }
                var summary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in request.Summary)
                {
                    if (!summary.TryAdd(SafeMetadata(pair.Key, 128), SafeMetadata(pair.Value, 2048))) { throw new IOException("Diagnostic summary contains duplicate keys."); }
                }
                await WriteEntryAsync(archive, "summary.json", JsonSerializer.Serialize(summary, JsonOptions), cancellationToken).ConfigureAwait(false);
                var manifest = new
                {
                    ApplicationName = application,
                    ApplicationVersion = version,
                    SessionId = session,
                    ExportedAtUtc = timestamp,
                    PrivacyMode = request.PrivacyMode.ToString(),
                    PrivacyNotice = request.PrivacyMode == SupportBundlePrivacyMode.Raw
                        ? "Raw logs may contain sensitive or identifying information."
                        : "Known sensitive fields in supported diagnostic text were redacted. Action results include only validated diagnostic fields. Unsupported sources are omitted; text sanitation cannot identify every possible secret.",
                    IncludedFiles = included,
                    OmittedFiles = omissions
                };
                await WriteEntryAsync(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, archivePath);
            return new(archivePath, included.Select(x => x.ArchiveEntryName).ToArray(), omissions.Select(x => x.FileName).ToArray());
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateSource(SupportBundleSource source, HashSet<string> names)
    {
        ArgumentNullException.ThrowIfNull(source);
        string name = source.ArchiveName;
        if (name is null || name.Length > 96 || !ArchiveNamePattern().IsMatch(name) || name.Contains("..", StringComparison.Ordinal) || !names.Add(name))
        { throw new IOException("invalid_archive_name"); }
        string extension = Path.GetExtension(source.Path);
        string archiveExtension = Path.GetExtension(name);
        bool valid = source.Format switch
        {
            SupportBundleSourceFormat.ApplicationText or SupportBundleSourceFormat.NativeText => IsText(extension) && IsText(archiveExtension),
            SupportBundleSourceFormat.ActionResultJson => extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && archiveExtension.Equals(".json", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!valid || !Path.IsPathFullyQualified(source.Path)) { throw new IOException("unsupported_source"); }
    }

    private static bool IsText(string extension) => extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads only the initial bounded length and rejects changed lengths rather than following a growing log.</summary>
    internal static async Task<byte[]> ReadBoundedSnapshotAsync(Stream stream, long remainingBytes, CancellationToken token, long? expectedLength = null)
    {
        long length = expectedLength ?? stream.Length;
        if (stream.Length != length) { throw new IOException("source_changed_during_snapshot"); }
        if (length > MaximumFileBytes || length > remainingBytes || length < 0) { throw new IOException("source_byte_limit"); }
        byte[] bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != length) { throw new IOException("source_changed_during_snapshot"); }
        return bytes;
    }

    private static string DecodeText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string SafeMetadata(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > maximumLength) { throw new IOException("Diagnostic metadata exceeds its supported bounds."); }
        return DiagnosticContentSanitizer.Sanitize(value, maximumLength);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content, CancellationToken token)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), token).ConfigureAwait(false);
    }

    private static string ResolveAvailablePath(string root, string name)
    {
        string candidate = Path.Combine(root, name + ".zip");
        for (int suffix = 2; File.Exists(candidate); suffix++) { candidate = Path.Combine(root, $"{name}-{suffix}.zip"); }
        return candidate;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveNamePattern();
    private sealed record IncludedSource(int SourceIndex, string SourceFileName, string ArchiveEntryName);
    private sealed record OmittedSource(int SourceIndex, string FileName, string Reason);
}
