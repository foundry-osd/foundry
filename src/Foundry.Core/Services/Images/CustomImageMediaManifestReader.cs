// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;

namespace Foundry.Core.Services.Images;

/// <summary>
/// Reads the custom image manifest a Deploy section binds, under a media root or a network cache share, and resolves the image
/// paths it lists. Foundry Deploy offers the images from it; Foundry OSD and Deploy check from it that the published images are
/// on a share.
/// </summary>
public static class CustomImageMediaManifestReader
{
    /// <summary>The largest custom image manifest a reader accepts.</summary>
    public const int MaximumManifestBytes = 16 * 1024 * 1024;

    // Same lenient reading rules the Deploy runtime has always applied to media manifests.
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Returns whether the section names a manifest by a file-name-safe id and a SHA-256, before any path is built from it.</summary>
    /// <param name="settings">The custom image section of a Deploy configuration or a network cache index.</param>
    public static bool HasValidBinding(DeployCustomImagesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !string.IsNullOrWhiteSpace(settings.ManifestId) &&
            settings.ManifestId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
            CustomImageSettingsValidator.IsValidHash(settings.ManifestHash);
    }

    /// <summary>
    /// Reads the manifest bound by <paramref name="settings"/> under one media root and checks its hash and every entry, so
    /// each returned entry is an included image at its canonical path. Returns <see langword="null"/> when the root has no
    /// such manifest. The caller checks the binding with <see cref="HasValidBinding"/> first.
    /// </summary>
    /// <param name="rootPath">The media root or the <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="settings">The section that binds the manifest.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="InvalidDataException">The manifest is redirected, oversized, does not match the bound hash, or describes an invalid image.</exception>
    /// <exception cref="JsonException">The manifest is not JSON.</exception>
    /// <exception cref="IOException">The manifest could not be read.</exception>
    public static async Task<CustomImageMediaManifest?> ReadAsync(string rootPath, DeployCustomImagesSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(settings);
        string manifestPath = Path.Combine(rootPath, CustomImageMediaPaths.RelativeRoot, "manifests", settings.ManifestId + ".json");
        if (!File.Exists(manifestPath)) return null;
        EnsureRegularPath(manifestPath);
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumManifestBytes) throw new InvalidDataException();
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException();
        CustomImageMediaManifest manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(bytes, Options)
            ?? throw new InvalidDataException();
        if (manifest.SchemaVersion != 1 || manifest.ManifestId != settings.ManifestId || manifest.Images is null ||
            manifest.Images.Any(entry => entry?.Reference is null || entry.RelativePath is null) ||
            manifest.Images.Count > CustomImageSettingsValidator.MaximumImages ||
            manifest.Images.Select(image => image.Reference.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Images.Count)
            throw new InvalidDataException();
        foreach (CustomImageMediaEntry entry in manifest.Images)
        {
            if (!CustomImageSettingsValidator.IsValidReference(entry.Reference) || !entry.Reference.IsIncluded)
                throw new InvalidDataException();
            string expectedPath = $"{CustomImageMediaPaths.RelativeRoot}/{entry.Reference.ContentHash.ToLowerInvariant()}/image.wim";
            if (!Normalize(entry.RelativePath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        }

        return manifest;
    }

    /// <summary>Rejects rooted paths, alternate streams and traversal before accessing media content.</summary>
    /// <param name="root">The media root.</param>
    /// <param name="relativePath">A path read from a manifest.</param>
    /// <exception cref="InvalidDataException">The path leaves the root or is not a plain relative path.</exception>
    public static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            Normalize(relativePath).Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("CustomImages.InvalidManifest");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CustomImages.InvalidManifest");
        return result;
    }

    /// <summary>Rejects redirection in the file and every parent directory.</summary>
    /// <param name="path">The file to open.</param>
    /// <exception cref="InvalidDataException">The file is a directory, or it or a parent directory is a reparse point.</exception>
    public static void EnsureRegularPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if ((File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("CustomImages.InvalidSource");
        for (DirectoryInfo? directory = Directory.GetParent(fullPath); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CustomImages.InvalidSource");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
