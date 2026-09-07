// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.IO;

namespace Foundry.Deploy.Services.Catalog;

public sealed class CatalogSnapshotStore
{
    private readonly string root;
    private readonly WinPeMediaManifest manifest;
    private readonly string? writableRoot;

    public CatalogSnapshotStore(string trustedMediaRoot, WinPeMediaManifest trustedManifest, string? writableSnapshotRoot = null)
    {
        root = Path.GetFullPath(trustedMediaRoot);
        manifest = trustedManifest with
        {
            Applications = trustedManifest.Applications.Select(app => app with { Files = Array.AsReadOnly(app.Files.ToArray()) }).ToArray(),
            CatalogSnapshots = trustedManifest.CatalogSnapshots.ToArray()
        };
        writableRoot = writableSnapshotRoot;
    }

    public async Task<VerifiedCatalogDocument> LoadAsync(string id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        WinPeMediaManifestStore.Validate(manifest);
        WinPeCatalogSnapshot descriptor = manifest.CatalogSnapshots.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidDataException("The requested catalog is not pinned by this boot image.");
        if (descriptor.RelativePath != VerifiedCatalogSources.GetRelativePath(id)) throw new InvalidDataException("The catalog path is invalid.");
        string path = Path.Combine(root, descriptor.RelativePath);
        RejectReparseAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length != descriptor.Length || stream.Length is < 1 or > VerifiedCatalogSnapshotAcquirer.MaximumBytes)
            throw new InvalidDataException("The local catalog size does not match this boot image.");
        byte[] bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var document = new VerifiedCatalogDocument(id, bytes, descriptor.Sha256, descriptor.Revision, descriptor.SourceUri, descriptor.RetrievedUtc);
        _ = VerifiedCatalogContent.Parse(document);
        return document;
    }

    public void Publish(VerifiedCatalogDocument document)
    {
        var captured = document with { Content = document.Content.ToArray() };
        if (captured.Id == VerifiedCatalogSources.OperatingSystems) _ = OperatingSystemCatalogService.ParseVerified(captured);
        else if (captured.Id == VerifiedCatalogSources.DriverPacks) _ = DriverPackCatalogService.ParseVerified(captured);
        else throw new InvalidDataException("Unknown catalog identity.");
        if (writableRoot is null) return;
        RejectReparseAncestors(writableRoot);
        Directory.CreateDirectory(writableRoot);
        string destination = Path.Combine(writableRoot, captured.Id + ".json");
        RejectReparseAncestors(destination);
        AtomicFile.WriteAllText(destination, JsonSerializer.Serialize(new SnapshotEnvelope(1, captured)));
    }

    private sealed record SnapshotEnvelope(int Version, VerifiedCatalogDocument Document);

    private static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Catalog paths cannot contain reparse points.");
        }
    }
}
