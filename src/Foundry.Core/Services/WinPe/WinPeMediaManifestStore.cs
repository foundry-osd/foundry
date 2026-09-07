// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.Media;

namespace Foundry.Core.Services.WinPe;

/// <summary>Creates and validates boot-pinned identities; writable sidecars never supply expected hashes.</summary>
public static class WinPeMediaManifestStore
{
    public const string RelativePath = "Foundry/Config/foundry.media.manifest.json";
    public const int MaximumManifestBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static WinPeMediaManifest Create(WinPePreparedRuntimePayloads prepared, MediaOperationTarget target,
        WinPeUsbDiskIdentity? intendedUsbIdentity, IReadOnlyList<VerifiedCatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(documents);
        ObjectDisposedException.ThrowIf(prepared.IsDisposed, prepared);
        var applications = prepared.Applications.Select(application => new WinPeRuntimeApplicationManifest(
            application.ApplicationName, application.RuntimeIdentifier, application.Source,
            application.ReleaseTag, application.ArchiveSha256, Array.AsReadOnly(application.Files.ToArray()))).ToArray();
        VerifiedCatalogDocument[] captured = CaptureDocuments(documents);
        var snapshots = captured.Select(document => new WinPeCatalogSnapshot(document.Id,
            VerifiedCatalogSources.GetRelativePath(document.Id), document.Sha256, document.Revision,
            document.SourceUri, document.RetrievedUtc, document.Content.LongLength)).ToArray();
        var manifest = new WinPeMediaManifest(1, prepared.MediaId, applications.FirstOrDefault()?.RuntimeIdentifier ?? "",
            target, intendedUsbIdentity, Array.AsReadOnly(applications), Array.AsReadOnly(snapshots));
        Validate(manifest);
        ValidateDocuments(manifest, captured);
        return manifest;
    }

    public static async Task<WinPeMediaManifest> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        RejectReparseAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > MaximumManifestBytes) throw Invalid("The media manifest exceeds its size limit.");
        byte[] bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
            throw Invalid("The media manifest changed while being read.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement);
            WinPeMediaManifest manifest = JsonSerializer.Deserialize<WinPeMediaManifest>(bytes, JsonOptions)
                ?? throw Invalid("The media manifest is empty.");
            Validate(manifest);
            return manifest;
        }
        catch (JsonException error) { throw new InvalidDataException("The media manifest is invalid JSON.", error); }
    }

    public static void Validate(WinPeMediaManifest manifest, string? expectedRuntimeIdentifier = null, Guid? expectedMediaId = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Version != 1 || manifest.MediaId == Guid.Empty ||
            manifest.RuntimeIdentifier is not ("win-x64" or "win-arm64") || !Enum.IsDefined(manifest.Target) ||
            expectedRuntimeIdentifier is not null && manifest.RuntimeIdentifier != expectedRuntimeIdentifier ||
            expectedMediaId is not null && manifest.MediaId != expectedMediaId)
            throw Invalid("The media manifest identity, version or architecture is invalid.");
        if (manifest.Target == MediaOperationTarget.Iso && manifest.IntendedUsbIdentity is not null ||
            manifest.Target != MediaOperationTarget.Iso && (manifest.IntendedUsbIdentity is not { Size: > 0 } disk ||
                string.IsNullOrWhiteSpace(disk.UniqueId) && string.IsNullOrWhiteSpace(disk.SerialNumber)))
            throw Invalid("The media manifest does not identify its intended media target.");
        if (manifest.Applications is null || manifest.Applications.Count is < 1 or > 2)
            throw Invalid("The media manifest has no supported runtime set.");
        var applicationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WinPeRuntimeApplicationManifest application in manifest.Applications)
        {
            if (application is null || application.ApplicationName is not ("Foundry.Connect" or "Foundry.Deploy") ||
                !applicationNames.Add(application.ApplicationName) || application.RuntimeIdentifier != manifest.RuntimeIdentifier ||
                !Enum.IsDefined(application.Source) || application.Files is null || application.Files.Count is < 1 or > 50000)
                throw Invalid("A runtime application identity is invalid or duplicated.");
            if (application.Source == WinPeProvisioningSource.Release &&
                (string.IsNullOrWhiteSpace(application.ReleaseTag) || !IsDigest(application.ArchiveSha256)) ||
                application.ArchiveSha256 is not null && !IsDigest(application.ArchiveSha256))
                throw Invalid("A runtime archive identity is invalid.");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WinPeRuntimeFile file in application.Files)
            {
                if (file is null) throw Invalid("A runtime file identity is missing.");
                ValidateRelativePath(file.RelativePath);
                if (!files.Add(file.RelativePath.Replace('\\', '/')) || file.Length < 0 || !IsDigest(file.Sha256))
                    throw Invalid("A runtime file identity is invalid or duplicated.");
            }
            if (!files.Contains(application.ApplicationName + ".exe")) throw Invalid("A runtime apphost is missing.");
            foreach (string path in files)
            {
                string[] parts = path.Split('/');
                for (int i = 1; i < parts.Length; i++)
                    if (files.Contains(string.Join('/', parts.Take(i)))) throw Invalid("Runtime file paths overlap directories.");
            }
        }
        if (!applicationNames.Contains("Foundry.Connect")) throw Invalid("Embedded Connect is required for every medium.");
        if (manifest.CatalogSnapshots is null || manifest.CatalogSnapshots.Count is < 1 or > 2)
            throw Invalid("The operating system catalog snapshot is required.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WinPeCatalogSnapshot catalog in manifest.CatalogSnapshots)
        {
            if (catalog is null || catalog.Id is not (VerifiedCatalogSources.OperatingSystems or VerifiedCatalogSources.DriverPacks) ||
                !ids.Add(catalog.Id)) throw Invalid("A catalog identity is invalid or duplicated.");
            if (catalog.RelativePath != VerifiedCatalogSources.GetRelativePath(catalog.Id) ||
                !IsDigest(catalog.Sha256) || catalog.Length is <= 0 or > 32 * 1024 * 1024 ||
                catalog.Revision != "sha256:" + catalog.Sha256.ToLowerInvariant() ||
                catalog.SourceUri is null || !catalog.SourceUri.IsAbsoluteUri || catalog.SourceUri.UserInfo.Length != 0 ||
                catalog.SourceUri.Fragment.Length != 0 || !string.Equals(catalog.SourceUri.AbsoluteUri,
                    VerifiedCatalogSources.GetUri(catalog.Id).AbsoluteUri, StringComparison.Ordinal) || catalog.RetrievedUtc == default)
                throw Invalid("A catalog snapshot does not bind its exact authenticated source bytes.");
        }
        if (!ids.Contains(VerifiedCatalogSources.OperatingSystems)) throw Invalid("The operating system catalog snapshot is required.");
    }

    public static async Task ValidateRuntimeAsync(WinPeMediaManifest manifest, string applicationName, string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        Validate(manifest);
        WinPeRuntimeApplicationManifest application = manifest.Applications.SingleOrDefault(item => item.ApplicationName == applicationName)
            ?? throw Invalid("The runtime is not pinned by this media manifest.");
        string root = Path.GetFullPath(runtimeRoot);
        RejectReparseAncestors(root);
        var expected = application.Files.ToDictionary(file => file.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw Invalid("Runtime reparse points are not allowed.");
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (!actual.Add(relative) || !expected.TryGetValue(relative, out WinPeRuntimeFile? file))
                    throw Invalid("The runtime contains an unpinned file.");
                await using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                if (input.Length != file.Length || !Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false))
                    .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw Invalid("A runtime file failed integrity validation.");
            }
        }
        if (actual.Count != expected.Count) throw Invalid("The runtime is missing pinned files.");
    }

    public static async Task StageAsync(WinPeMediaManifest manifest, IReadOnlyList<VerifiedCatalogDocument> documents,
        string mountedRoot, CancellationToken cancellationToken = default)
    {
        // Capture caller-owned arrays before the first await; expectations come only from verified preparation.
        byte[] manifestBytes = Serialize(manifest);
        manifest = JsonSerializer.Deserialize<WinPeMediaManifest>(manifestBytes, JsonOptions)!;
        VerifiedCatalogDocument[] captured = CaptureDocuments(documents);
        ValidateDocuments(manifest, captured);
        string root = Path.GetFullPath(mountedRoot);
        RejectReparseAncestors(root);
        foreach (WinPeCatalogSnapshot descriptor in manifest.CatalogSnapshots)
        {
            string destination = ResolvePath(root, descriptor.RelativePath);
            RejectReparseAncestors(destination);
        }
        string manifestPath = ResolvePath(root, RelativePath);
        RejectReparseAncestors(manifestPath);
        foreach (VerifiedCatalogDocument document in captured)
        {
            string destination = ResolvePath(root, VerifiedCatalogSources.GetRelativePath(document.Id));
            RejectReparseAncestors(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, document.Content, cancellationToken).ConfigureAwait(false);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken).ConfigureAwait(false);
    }

    public static byte[] Serialize(WinPeMediaManifest manifest)
    {
        Validate(manifest);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (bytes.Length > MaximumManifestBytes) throw Invalid("The media manifest exceeds its size limit.");
        return bytes;
    }

    private static VerifiedCatalogDocument[] CaptureDocuments(IReadOnlyList<VerifiedCatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return documents.Select(document => document is null || document.Content is null
            ? throw Invalid("A verified catalog document is missing.")
            : document with { Content = document.Content.ToArray() }).ToArray();
    }

    private static void ValidateDocuments(WinPeMediaManifest manifest, IReadOnlyList<VerifiedCatalogDocument> documents)
    {
        if (documents.Count != manifest.CatalogSnapshots.Count || documents.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != documents.Count)
            throw Invalid("Catalog documents do not match the pinned catalog set.");
        foreach (WinPeCatalogSnapshot descriptor in manifest.CatalogSnapshots)
        {
            VerifiedCatalogDocument document = documents.SingleOrDefault(item => item.Id == descriptor.Id)
                ?? throw Invalid("A pinned catalog document is missing.");
            if (document.Content.LongLength != descriptor.Length || document.Sha256 != descriptor.Sha256 ||
                document.Revision != descriptor.Revision || !string.Equals(document.SourceUri?.AbsoluteUri, descriptor.SourceUri.AbsoluteUri, StringComparison.Ordinal) ||
                document.RetrievedUtc != descriptor.RetrievedUtc || !Convert.ToHexString(SHA256.HashData(document.Content))
                    .Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Catalog bytes differ from their authenticated snapshot.");
        }
    }

    private static string ResolvePath(string root, string relative)
    {
        ValidateRelativePath(relative);
        string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw Invalid("A media path escapes its root.");
        return result;
    }

    private static void ValidateRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || Path.IsPathRooted(path) ||
            path.Contains(':') || path.Any(character => character < 32 || "<>\"|?*".Contains(character)))
            throw Invalid("A media path is unsafe.");
        foreach (string part in path.Replace('\\', '/').Split('/'))
        {
            string stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsAsciiDigit(stem[3]))
                throw Invalid("A media path contains an unsafe component.");
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Invalid("Media reparse points are not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("The media manifest contains duplicate properties.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static InvalidDataException Invalid(string message) => new(message);
}
