// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Creates an atomic, manifest-driven diagnostic archive from flushed log snapshots.
/// </summary>
public sealed partial class SupportBundleExporter(TimeProvider? timeProvider = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Creates a temporary archive and publishes it only after every selected source has been processed.
    /// </summary>
    public async Task<SupportBundleResult> ExportAsync(
        SupportBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationDirectoryPath);
        ArgumentNullException.ThrowIfNull(request.LogFilePaths);

        Directory.CreateDirectory(request.DestinationDirectoryPath);
        DateTimeOffset exportedAtUtc = _timeProvider.GetUtcNow();
        string safeApplicationName = SanitizeFileName(request.ApplicationName);
        string archiveName = $"FoundrySupport-{safeApplicationName}-{exportedAtUtc:yyyyMMddTHHmmssZ}.zip";
        string archivePath = ResolveAvailablePath(request.DestinationDirectoryPath, archiveName);
        string temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        var includedFiles = new List<SupportBundleIncludedFile>();
        var omittedFiles = new List<SupportBundleOmission>();

        try
        {
            await using (FileStream archiveStream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                65536,
                FileOptions.Asynchronous))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int sourceIndex = 0;
                foreach (string sourcePath in request.LogFilePaths)
                {
                    sourceIndex++;
                    cancellationToken.ThrowIfCancellationRequested();
                    string sourceName = Path.GetFileName(sourcePath);
                    try
                    {
                        string content = await ReadSharedTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                        if (request.PrivacyMode == SupportBundlePrivacyMode.Sanitized)
                        {
                            content = DiagnosticContentSanitizer.SanitizeMultiline(content, int.MaxValue);
                        }

                        string publishedSourceName = request.PrivacyMode == SupportBundlePrivacyMode.Sanitized
                            ? DiagnosticContentSanitizer.Sanitize(sourceName, int.MaxValue)
                            : sourceName;
                        string entryName = ResolveEntryName(usedEntryNames, publishedSourceName);
                        await WriteTextEntryAsync(archive, $"logs/{entryName}", content, cancellationToken).ConfigureAwait(false);
                        includedFiles.Add(new SupportBundleIncludedFile(sourceIndex, publishedSourceName, entryName));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        if (request.PrivacyMode == SupportBundlePrivacyMode.Sanitized)
                        {
                            throw;
                        }

                        omittedFiles.Add(new SupportBundleOmission(sourceIndex, sourceName, ex.GetType().Name));
                    }
                }

                IReadOnlyDictionary<string, string> summary = request.Summary.ToDictionary(
                    static item => DiagnosticContentSanitizer.Sanitize(item.Key, int.MaxValue),
                    static item => DiagnosticContentSanitizer.Sanitize(item.Value, int.MaxValue),
                    StringComparer.OrdinalIgnoreCase);

                await WriteJsonEntryAsync(archive, "summary.json", summary, cancellationToken).ConfigureAwait(false);
                var manifest = new SupportBundleManifest(
                    request.ApplicationName,
                    request.ApplicationVersion,
                    request.SessionId,
                    exportedAtUtc,
                    request.PrivacyMode.ToString(),
                    request.PrivacyMode == SupportBundlePrivacyMode.Raw
                        ? "Raw logs may contain sensitive or identifying information."
                        : "Sensitive identifiers, credentials, query strings, email addresses, and user profile names were redacted.",
                    includedFiles,
                    omittedFiles);
                await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, archivePath);
            return new SupportBundleResult(
                archivePath,
                includedFiles.Select(static file => file.ArchiveEntryName).ToArray(),
                omittedFiles.Select(static omission => omission.FileName).ToArray());
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async Task<string> ReadSharedTextAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return WriteTextEntryAsync(archive, entryName, json, cancellationToken);
    }

    private static string ResolveEntryName(HashSet<string> usedEntryNames, string sourceName)
    {
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(sourceName) ? "diagnostic.log" : sourceName);
        if (usedEntryNames.Add(safeName))
        {
            return safeName;
        }

        string baseName = Path.GetFileNameWithoutExtension(safeName);
        string extension = Path.GetExtension(safeName);
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseName}-{suffix}{extension}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string ResolveAvailablePath(string destinationDirectoryPath, string archiveName)
    {
        string candidate = Path.Combine(destinationDirectoryPath, archiveName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string baseName = Path.GetFileNameWithoutExtension(archiveName);
        for (int suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(destinationDirectoryPath, $"{baseName}-{suffix}.zip");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            builder.Append(invalidCharacters.Contains(character) || char.IsWhiteSpace(character) ? '-' : character);
        }

        return builder.Length == 0 ? "Foundry" : builder.ToString();
    }

    private sealed record SupportBundleManifest(
        string ApplicationName,
        string ApplicationVersion,
        string SessionId,
        DateTimeOffset ExportedAtUtc,
        string PrivacyMode,
        string PrivacyNotice,
        IReadOnlyList<SupportBundleIncludedFile> IncludedFiles,
        IReadOnlyList<SupportBundleOmission> OmittedFiles);

    private sealed record SupportBundleIncludedFile(int SourceIndex, string SourceFileName, string ArchiveEntryName);

    private sealed record SupportBundleOmission(int SourceIndex, string FileName, string Reason);
}
