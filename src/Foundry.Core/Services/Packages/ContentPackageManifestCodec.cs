// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Packages;

namespace Foundry.Core.Services.Packages;

/// <summary>Provides one canonical content identity shared by authoring, media publication and initial target verification.</summary>
/// <remarks>
/// Canonical bytes and hashes do not depend on the package kind: <see cref="Serialize"/> only checks structure.
/// Kind limits (counts and path lengths) are applied by <see cref="Validate"/> and <see cref="Deserialize"/>.
/// </remarks>
public static class ContentPackageManifestCodec
{
    public const int MaximumManifestBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static byte[] Serialize(ContentPackageManifest manifest)
    {
        ValidateStructure(manifest);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest with
        {
            Directories = manifest.Directories.Order(StringComparer.Ordinal).ToArray(),
            Files = manifest.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => file with { Sha256 = file.Sha256.ToLowerInvariant() }).ToArray()
        }, Options);
        if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("ContentPackage.PackageManifestTooLarge");
        return bytes;
    }

    public static string GetContentHash(ContentPackageManifest manifest) => Convert.ToHexStringLower(SHA256.HashData(Serialize(manifest)));

    public static ContentPackageManifest Deserialize(ReadOnlySpan<byte> bytes, string expectedHash, ContentPackageKind kind)
    {
        if (bytes.Length is 0 or > MaximumManifestBytes || !ContentPackagePathPolicy.IsValidHash(expectedHash) ||
            !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
        try
        {
            ContentPackageManifest manifest = JsonSerializer.Deserialize<ContentPackageManifest>(bytes, Options)
                ?? throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
            Validate(manifest, kind);
            if (!bytes.SequenceEqual(Serialize(manifest))) throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
            return manifest;
        }
        catch (JsonException exception) { throw new InvalidDataException("ContentPackage.InvalidPackageManifest", exception); }
    }

    /// <summary>Validates the manifest structure and the count and path length limits of <paramref name="kind"/>.</summary>
    public static void Validate(ContentPackageManifest? manifest, ContentPackageKind kind)
    {
        ValidateStructure(manifest);
        if (manifest!.Files.Count > kind.MaximumFiles || manifest.Directories.Count > kind.MaximumDirectories)
            throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
        var policy = new ContentPackagePathPolicy(kind);
        foreach (string directory in manifest.Directories) policy.ValidateRelativePath(directory);
        foreach (ContentPackageFile file in manifest.Files) policy.ValidateRelativePath(file.RelativePath);
    }

    private static void ValidateStructure(ContentPackageManifest? manifest)
    {
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Files is null || manifest.Directories is null || manifest.Files.Count == 0)
            throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in manifest.Directories)
        {
            ContentPackagePathPolicy.ValidatePathSyntax(directory);
            if (!names.Add(directory)) throw new InvalidDataException("ContentPackage.DuplicatePackagePath");
            directories.Add(directory);
        }
        long total = 0;
        foreach (ContentPackageFile? file in manifest.Files)
        {
            if (file is null || file.Length < 0 || !ContentPackagePathPolicy.IsValidHash(file.Sha256))
                throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
            ContentPackagePathPolicy.ValidatePathSyntax(file.RelativePath);
            if (!names.Add(file.RelativePath)) throw new InvalidDataException("ContentPackage.DuplicatePackagePath");
            try { total = checked(total + file.Length); }
            catch (OverflowException exception) { throw new InvalidDataException("ContentPackage.InvalidPackageManifest", exception); }
        }
        foreach (string path in names)
        {
            int separator = path.LastIndexOf('/');
            if (separator >= 0 && !directories.Contains(path[..separator])) throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
        }
    }
}
