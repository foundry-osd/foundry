// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Packages;

namespace Foundry.Core.Services.Packages;

/// <summary>Outcome of looking up one authenticated media generation.</summary>
public enum ContentPackageMediaStatus
{
    /// <summary>No candidate root holds the referenced media manifest.</summary>
    NotFound,

    /// <summary>A manifest exists with the referenced id, but its size or SHA-256 differs from the binding.</summary>
    HashMismatch,

    /// <summary>The binding or the verified manifest does not satisfy the package kind rules.</summary>
    Incompatible,

    /// <summary>A verified media manifest was found.</summary>
    Found
}

/// <summary>Result of <see cref="ContentPackageMediaReader.ReadAsync"/>; Root and Manifest are set only when Found.</summary>
public sealed record ContentPackageMediaReadResult(ContentPackageMediaStatus Status, string? Root, ContentPackageMediaManifest? Manifest);

/// <summary>
/// Finds and verifies the media manifest that binds package snapshots to one media generation.
/// Candidate roots are supplied by the caller; the reader never enumerates volumes itself.
/// </summary>
public sealed class ContentPackageMediaReader(ContentPackageKind kind)
{
    // Same lenient reading rules the Deploy runtime has always applied to media manifests.
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Returns the first candidate root whose manifest matches the bound id and SHA-256 and satisfies the kind rules.</summary>
    public async Task<ContentPackageMediaReadResult> ReadAsync(IEnumerable<string> candidateRoots, string? manifestId, string? manifestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateRoots);
        if (!Guid.TryParseExact(manifestId, "N", out _) || !ContentPackagePathPolicy.IsValidHash(manifestHash))
            return Result(ContentPackageMediaStatus.Incompatible);
        bool mismatch = false;
        foreach (string root in candidateRoots)
        {
            string path = Path.Combine(root, kind.MediaRoot.Replace('/', Path.DirectorySeparatorChar), "manifests", manifestId + ".json");
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > kind.MaximumMediaManifestBytes) { mismatch = true; continue; }
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length > kind.MaximumMediaManifestBytes || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifestHash, StringComparison.OrdinalIgnoreCase))
            {
                mismatch = true;
                continue;
            }
            ContentPackageMediaManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<ContentPackageMediaManifest>(bytes, Options); }
            catch (JsonException) { return Result(ContentPackageMediaStatus.Incompatible); }
            if (manifest is null || manifest.SchemaVersion != 1 || manifest.Id != manifestId || !HasCanonicalPackages(manifest))
                return Result(ContentPackageMediaStatus.Incompatible);
            return new(ContentPackageMediaStatus.Found, root, manifest);
        }
        return Result(mismatch ? ContentPackageMediaStatus.HashMismatch : ContentPackageMediaStatus.NotFound);
    }

    private static ContentPackageMediaReadResult Result(ContentPackageMediaStatus status) => new(status, null, null);

    private bool HasCanonicalPackages(ContentPackageMediaManifest manifest)
    {
        try
        {
            foreach (var package in manifest.Packages)
            {
                if (package.RelativePath != $"{kind.MediaRoot}/Packages/{package.ContentHash}/files") return false;
                ContentPackageManifestCodec.Validate(package.Manifest, kind);
                if (!ContentPackageManifestCodec.GetContentHash(package.Manifest).Equals(package.ContentHash, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
}
