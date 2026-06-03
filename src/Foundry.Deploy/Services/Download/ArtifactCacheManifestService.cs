using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Download;

internal static class ArtifactCacheManifestService
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static bool TryValidate(
        string artifactPath,
        string expectedHash,
        string hashAlgorithm,
        long? expectedSizeBytes,
        ILogger logger,
        out ArtifactCacheManifest? manifest)
    {
        manifest = null;
        string manifestPath = GetManifestPath(artifactPath);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ArtifactCacheManifest>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to read artifact cache manifest. ManifestPath={ManifestPath}", manifestPath);
            return false;
        }

        if (manifest is null ||
            manifest.Version != CurrentVersion ||
            !string.Equals(manifest.HashAlgorithm, hashAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.ExpectedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedSizeBytes is > 0 && manifest.ExpectedSizeBytes != expectedSizeBytes.Value)
        {
            return false;
        }

        FileInfo artifact = new(artifactPath);
        if (!artifact.Exists ||
            manifest.FileSizeBytes != artifact.Length ||
            manifest.FileLastWriteTimeUtc != new DateTimeOffset(artifact.LastWriteTimeUtc, TimeSpan.Zero))
        {
            return false;
        }

        return true;
    }

    public static async Task WriteAsync(
        string artifactPath,
        string sourceUrl,
        string? artifactKind,
        string expectedHash,
        string hashAlgorithm,
        long? expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        FileInfo artifact = new(artifactPath);
        var manifest = new ArtifactCacheManifest
        {
            ArtifactKind = artifactKind,
            SourceUrl = sourceUrl,
            HashAlgorithm = hashAlgorithm,
            ExpectedHash = expectedHash,
            ExpectedSizeBytes = expectedSizeBytes,
            FileSizeBytes = artifact.Length,
            FileLastWriteTimeUtc = new DateTimeOffset(artifact.LastWriteTimeUtc, TimeSpan.Zero),
            ValidatedAtUtc = DateTimeOffset.UtcNow
        };

        await using FileStream stream = File.Create(GetManifestPath(artifactPath));
        await JsonSerializer
            .SerializeAsync(stream, manifest, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public static string GetManifestPath(string artifactPath)
    {
        return $"{artifactPath}.manifest.json";
    }
}
