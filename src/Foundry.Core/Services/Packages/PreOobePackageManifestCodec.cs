// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.PreOobe;

namespace Foundry.Core.Services.Packages;

/// <summary>Provides one canonical content identity shared by authoring, media publication and initial target verification.</summary>
public static class PreOobePackageManifestCodec
{
    public const int MaximumManifestBytes = 8 * 1024 * 1024;
    public const int MaximumFiles = 10_000;
    public const int MaximumDirectories = 10_000;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static byte[] Serialize(PreOobePackageManifest manifest)
    {
        Validate(manifest);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest with
        {
            Directories = manifest.Directories.Order(StringComparer.Ordinal).ToArray(),
            Files = manifest.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => file with { Sha256 = file.Sha256.ToLowerInvariant() }).ToArray()
        }, Options);
        if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("PreOobe.PackageManifestTooLarge");
        return bytes;
    }

    public static string GetContentHash(PreOobePackageManifest manifest) => Convert.ToHexStringLower(SHA256.HashData(Serialize(manifest)));

    public static PreOobePackageManifest Deserialize(ReadOnlySpan<byte> bytes, string expectedHash)
    {
        if (bytes.Length is 0 or > MaximumManifestBytes || !PreOobePackagePathPolicy.IsValidHash(expectedHash) ||
            !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PreOobe.InvalidPackageManifest");
        try
        {
            PreOobePackageManifest manifest = JsonSerializer.Deserialize<PreOobePackageManifest>(bytes, Options)
                ?? throw new InvalidDataException("PreOobe.InvalidPackageManifest");
            if (!bytes.SequenceEqual(Serialize(manifest))) throw new InvalidDataException("PreOobe.InvalidPackageManifest");
            return manifest;
        }
        catch (JsonException exception) { throw new InvalidDataException("PreOobe.InvalidPackageManifest", exception); }
    }

    public static void Validate(PreOobePackageManifest? manifest)
    {
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Files is null || manifest.Directories is null ||
            manifest.Files.Count is 0 or > MaximumFiles || manifest.Directories.Count > MaximumDirectories)
            throw new InvalidDataException("PreOobe.InvalidPackageManifest");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in manifest.Directories)
        {
            PreOobePackagePathPolicy.ValidateRelativePath(directory);
            if (!names.Add(directory)) throw new InvalidDataException("PreOobe.DuplicatePackagePath");
            directories.Add(directory);
        }
        long total = 0;
        foreach (PreOobePackageFile? file in manifest.Files)
        {
            if (file is null || file.Length < 0 || !PreOobePackagePathPolicy.IsValidHash(file.Sha256))
                throw new InvalidDataException("PreOobe.InvalidPackageManifest");
            PreOobePackagePathPolicy.ValidateRelativePath(file.RelativePath);
            if (!names.Add(file.RelativePath)) throw new InvalidDataException("PreOobe.DuplicatePackagePath");
            try { total = checked(total + file.Length); }
            catch (OverflowException exception) { throw new InvalidDataException("PreOobe.InvalidPackageManifest", exception); }
        }
        foreach (string path in names)
        {
            int separator = path.LastIndexOf('/');
            if (separator >= 0 && !directories.Contains(path[..separator])) throw new InvalidDataException("PreOobe.InvalidPackageManifest");
        }
    }
}
