// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;

namespace Foundry.Core.Services.Packages;

/// <summary>Imports owned immutable package snapshots of one <see cref="ContentPackageKind"/> separately from portable deployment profiles.</summary>
public sealed class ContentPackageLibraryService
{
    private const int CopyBufferSize = 128 * 1024;
    private readonly string root;
    private readonly ContentPackagePathPolicy paths;

    /// <summary>Creates a library whose limits and import rules come from <paramref name="kind"/>.</summary>
    /// <param name="rootDirectory">Host directory that owns the library layout.</param>
    /// <param name="kind">Kind that supplies path, count and INF rules.</param>
    public ContentPackageLibraryService(string rootDirectory, ContentPackageKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(kind);
        root = Path.GetFullPath(rootDirectory);
        Kind = kind;
        paths = new ContentPackagePathPolicy(kind);
    }

    /// <summary>Kind whose limits and rules this library applies.</summary>
    public ContentPackageKind Kind { get; }

    /// <summary>Performs lightweight readiness checks; consumers must acquire a fully verified lease before use.</summary>
    public bool IsAvailable(ContentPackageReference reference)
    {
        if (!IsValidReference(reference, Kind)) return false;
        try
        {
            string directory = ContentPath(reference.ContentHash);
            ContentPackageManifest manifest = ReadManifest(directory, reference.ContentHash);
            return manifest.Files.Count == reference.FileCount && manifest.Files.Sum(file => file.Length) == reference.Length &&
                manifest.Files.All(file => File.Exists(paths.Resolve(Path.Combine(directory, "files"), file.RelativePath)) &&
                    new FileInfo(paths.Resolve(Path.Combine(directory, "files"), file.RelativePath)).Length == file.Length);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException) { return false; }
    }

    /// <summary>Reads the manifest of an imported package for display metadata, without hashing its files or taking the library lock.</summary>
    /// <returns>The manifest, or <see langword="null"/> when the package is missing, damaged or the hash is invalid.</returns>
    public ContentPackageManifest? TryReadManifest(string contentHash)
    {
        try { return ReadManifest(ContentPath(contentHash), contentHash); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>
    /// Lists the lowercase content hashes that have a directory in the library, without verifying them or taking the library lock.
    /// Lets a caller tell an entry an import created from one that already existed.
    /// </summary>
    internal IReadOnlySet<string> ListContentHashes()
    {
        string content = Path.Combine(root, "content");
        return Directory.Exists(content)
            ? Directory.EnumerateDirectories(content).Select(Path.GetFileName).OfType<string>().Where(ContentPackagePathPolicy.IsValidHash)
                .Select(hash => hash.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Checks a reference against the limits of <paramref name="kind"/> instead of the post-installation configuration limits.</summary>
    internal static bool IsValidReference(ContentPackageReference reference, ContentPackageKind kind) =>
        ContentPackagePathPolicy.IsValidHash(reference.ContentHash) && reference.Length >= 0 && reference.FileCount > 0 && reference.FileCount <= kind.MaximumFiles &&
        !string.IsNullOrWhiteSpace(reference.DisplayName) && reference.DisplayName.Length <= 256 && !reference.DisplayName.Any(char.IsControl);

    /// <summary>Freezes a file or folder without modifying the source. Identical bytes and relative names reuse one version.</summary>
    public Task<ContentPackageReference> ImportAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        ImportAsync(sourcePath, null, cancellationToken);

    /// <summary>
    /// Freezes a file or folder without modifying the source and reports the copy and verification of its bytes.
    /// Identical bytes and relative names reuse one version; reporting never changes the content hash or the manifest.
    /// </summary>
    /// <param name="sourcePath">File or folder to import.</param>
    /// <param name="progress">Receives throttled reports on the import thread, ending with the totals when every file is verified.</param>
    /// <param name="cancellationToken">Stops the import between copy chunks; pending content is removed before the task completes.</param>
    public async Task<ContentPackageReference> ImportAsync(string sourcePath, IProgress<ContentPackageImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(sourcePath);
        ContentPackagePathPolicy.ValidateNoReparsePoints(source);
        if (IsWithin(source, root) || (Directory.Exists(source) && IsWithin(root, source)))
            throw new InvalidDataException("ContentPackage.PackageSourceOverlapsLibrary");
        bool isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source)) throw new FileNotFoundException("ContentPackage.PackageSourceMissing");
        string sourceRoot = isDirectory ? source : Path.GetDirectoryName(source)!;
        SourceInventory inventory = isDirectory ? Inventory(sourceRoot) : new([], [Path.GetFileName(source)]);
        if (inventory.Files.Count == 0) throw new InvalidDataException("ContentPackage.EmptyPackage");
        if (Kind.RequiresInfFile && !inventory.Files.Any(file => file.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("ContentPackage.MissingInfFile");
        // Metadata lengths only size the progress; the manifest keeps the lengths read from the open handles.
        long totalBytes = progress is null ? 0 : inventory.Files.Sum(file => new FileInfo(paths.Resolve(sourceRoot, file)).Length);
        var reporter = new ImportProgressReporter(progress, totalBytes, inventory.Files.Count);
        using FileStream libraryLock = AcquireLibraryLock();
        string pending = Path.Combine(root, "pending", Guid.NewGuid().ToString("N"));
        string filesRoot = Path.Combine(pending, "files");
        Directory.CreateDirectory(filesRoot);
        List<FileStream> sourceHandles = [];
        try
        {
            foreach (string directory in inventory.Directories) Directory.CreateDirectory(paths.Resolve(filesRoot, directory));
            List<ContentPackageFile> files = [];
            byte[] buffer = new byte[CopyBufferSize];
            foreach (string relativePath in inventory.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileStream input = OpenRead(paths.Resolve(sourceRoot, relativePath));
                sourceHandles.Add(input);
                if (new DriveInfo(Path.GetPathRoot(filesRoot)!).AvailableFreeSpace < input.Length)
                    throw new IOException("ContentPackage.InsufficientPackageSpace");
                string target = paths.Resolve(filesRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string hash;
                await using (FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    hash = await HashAsync(input, output, buffer, reporter, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                input.Position = 0;
                if (!hash.Equals(await HashAsync(input, null, buffer, reporter, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal))
                    throw new InvalidDataException("ContentPackage.PackageSourceChanged");
                await using (FileStream copied = OpenRead(target))
                {
                    if (copied.Length != input.Length || !hash.Equals(await HashAsync(copied, null, buffer, reporter, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal))
                        throw new InvalidDataException("ContentPackage.PackageSourceChanged");
                }
                files.Add(new() { RelativePath = relativePath, Length = input.Length, Sha256 = hash });
                reporter.CompleteFile();
            }
            reporter.Complete();
            if (isDirectory && !SameInventory(inventory, Inventory(sourceRoot))) throw new InvalidDataException("ContentPackage.PackageSourceChanged");
            var manifest = new ContentPackageManifest { Directories = inventory.Directories, Files = files };
            byte[] manifestBytes = ContentPackageManifestCodec.Serialize(manifest);
            string contentHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            string destination = ContentPath(contentHash);
            ValidateDestination(manifest, Path.Combine(destination, "files"));
            await WriteManifestAsync(Path.Combine(pending, "manifest.json"), manifestBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                try
                {
                    await using ContentPackageLease existing = await AcquireCoreAsync(contentHash, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
                {
                    RepairOwnedRevision(destination, pending, contentHash, cancellationToken);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.Move(pending, destination);
            }
            return CreateReference(manifest, contentHash, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
        finally
        {
            foreach (FileStream handle in sourceHandles) handle.Dispose();
            if (Directory.Exists(pending)) DeleteOwnedTree(pending);
        }
    }

    /// <summary>Locks publication/deletion while acquiring read handles and verifies all content before returning.</summary>
    public async Task<ContentPackageLease> AcquireAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream libraryLock = AcquireLibraryLock();
        return await AcquireCoreAsync(contentHash, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes only an unreferenced, verified, unleased owned snapshot. Unknown files prevent removal.</summary>
    public async Task DeleteAsync(string contentHash, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referencedHashes);
        if (referencedHashes.Contains(contentHash, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("ContentPackage.PackageInUse");
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream libraryLock = AcquireLibraryLock();
        string directory = ContentPath(contentHash);
        if (!Directory.Exists(directory)) return;
        await using (ContentPackageLease validation = await AcquireCoreAsync(contentHash, cancellationToken, verifyContent: false).ConfigureAwait(false)) { }
        ContentPackageManifest manifest = ReadManifest(directory, contentHash);
        List<FileStream> deleteHandles = [];
        try
        {
            foreach (ContentPackageFile file in manifest.Files)
                deleteHandles.Add(new(paths.Resolve(Path.Combine(directory, "files"), file.RelativePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            deleteHandles.Add(new(Path.Combine(directory, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FileStream handle in deleteHandles) File.Delete(handle.Name);
            foreach (FileStream handle in deleteHandles) handle.Dispose();
            foreach (string child in manifest.Directories.OrderByDescending(path => path.Length))
                Directory.Delete(paths.Resolve(Path.Combine(directory, "files"), child), recursive: false);
            Directory.Delete(Path.Combine(directory, "files"), recursive: false);
            Directory.Delete(directory, recursive: false);
        }
        finally { foreach (FileStream handle in deleteHandles) handle.Dispose(); }
    }

    /// <summary>Checks actual library and target prefixes against installer path limits before copying.</summary>
    public void ValidateDestination(ContentPackageManifest manifest, string destinationRoot)
    {
        ContentPackageManifestCodec.Validate(manifest, Kind);
        foreach (string path in manifest.Directories.Concat(manifest.Files.Select(file => file.RelativePath)))
            _ = paths.Resolve(destinationRoot, path);
    }

    private async Task<ContentPackageLease> AcquireCoreAsync(string hash, CancellationToken cancellationToken, bool verifyContent = true)
    {
        string directory = ContentPath(hash);
        string filesRoot = Path.Combine(directory, "files");
        List<FileStream> handles = [];
        try
        {
            FileStream metadata = OpenRead(Path.Combine(directory, "manifest.json"));
            handles.Add(metadata);
            if (metadata.Length is 0 or > ContentPackageManifestCodec.MaximumManifestBytes) throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
            byte[] bytes = new byte[checked((int)metadata.Length)];
            await metadata.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            ContentPackageManifest manifest = ContentPackageManifestCodec.Deserialize(bytes, hash, Kind);
            ValidateDestination(manifest, filesRoot);
            SourceInventory inventory = Inventory(filesRoot);
            if (!SameInventory(new(manifest.Directories, manifest.Files.Select(file => file.RelativePath).ToArray()), inventory) ||
                Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) is not ("files" or "manifest.json")))
                throw new InvalidDataException("ContentPackage.PackageContentChanged");
            List<ContentPackageSourceFile> sources = [];
            foreach (ContentPackageFile file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = paths.Resolve(filesRoot, file.RelativePath);
                FileStream input = OpenRead(path);
                handles.Add(input);
                if (verifyContent && (input.Length != file.Length || !Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("ContentPackage.PackageContentChanged");
                input.Position = 0;
                sources.Add(new(path, file.RelativePath, file.Length, file.Sha256));
            }
            return new(CreateReference(manifest, hash.ToLowerInvariant(), hash.ToLowerInvariant()), manifest, filesRoot, sources, handles);
        }
        catch { foreach (FileStream handle in handles) handle.Dispose(); throw; }
    }

    private static ContentPackageReference CreateReference(ContentPackageManifest manifest, string hash, string name) => new()
    {
        ContentHash = hash,
        DisplayName = name,
        Length = manifest.Files.Sum(file => file.Length),
        FileCount = manifest.Files.Count
    };

    /// <summary>Replaces a damaged known revision only when no consumer holds its content and no unmanaged entries would be removed.</summary>
    private void RepairOwnedRevision(string destination, string pending, string hash, CancellationToken cancellationToken)
    {
        ContentPackageManifest manifest = ReadManifest(destination, hash);
        string filesRoot = Path.Combine(destination, "files");
        SourceInventory inventory = Directory.Exists(filesRoot) ? Inventory(filesRoot) : new([], []);
        var ownedFiles = manifest.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        if (inventory.Files.Any(path => !ownedFiles.Contains(path)) ||
            inventory.Directories.Any(path => !manifest.Directories.Contains(path, StringComparer.Ordinal)) ||
            Directory.EnumerateFileSystemEntries(destination).Any(path => Path.GetFileName(path) is not ("files" or "manifest.json")) ||
            File.Exists(filesRoot))
            throw new InvalidDataException("ContentPackage.PackageContentChanged");

        string retired = Path.Combine(root, "pending", "retired-" + Guid.NewGuid().ToString("N"));
        List<FileStream> replacementHandles = [];
        try
        {
            // Existing leases deny write/delete sharing. Hold all replacement handles before changing any path.
            replacementHandles.Add(new(Path.Combine(destination, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            foreach (string relative in inventory.Files)
                replacementHandles.Add(new(paths.Resolve(filesRoot, relative), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { foreach (FileStream handle in replacementHandles) handle.Dispose(); }

        // Windows cannot rename a directory with open children; the caller's library lock still excludes new leases.
        ContentPackagePathPolicy.ValidateNoReparsePoints(destination);
        Directory.Move(destination, retired);
        try { Directory.Move(pending, destination); }
        catch
        {
            Directory.Move(retired, destination);
            throw;
        }

        // Delete only the recorded old entries. Unexpected additions stay in the retired directory.
        foreach (string relative in inventory.Files)
            File.Delete(paths.Resolve(Path.Combine(retired, "files"), relative));
        foreach (string relative in inventory.Directories.OrderByDescending(path => path.Length))
            Directory.Delete(paths.Resolve(Path.Combine(retired, "files"), relative), recursive: false);
        if (Directory.Exists(Path.Combine(retired, "files"))) Directory.Delete(Path.Combine(retired, "files"), recursive: false);
        ContentPackagePathPolicy.ValidateNoReparsePoints(Path.Combine(retired, "manifest.json"));
        File.Delete(Path.Combine(retired, "manifest.json"));
        Directory.Delete(retired, recursive: false);
    }

    private string ContentPath(string hash)
    {
        if (!ContentPackagePathPolicy.IsValidHash(hash)) throw new InvalidDataException("ContentPackage.InvalidPackageReference");
        string path = Path.Combine(root, "content", hash.ToLowerInvariant());
        ContentPackagePathPolicy.ValidateNoReparsePoints(path);
        return path;
    }

    private FileStream AcquireLibraryLock()
    {
        ContentPackagePathPolicy.ValidateNoReparsePoints(root);
        Directory.CreateDirectory(root);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        foreach (SecurityIdentifier principal in new[]
        {
            identity.User ?? throw new InvalidOperationException("ContentPackage.UserUnavailable"),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        })
            security.AddAccessRule(new(principal, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);
        return new(Path.Combine(root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private ContentPackageManifest ReadManifest(string directory, string hash)
    {
        using FileStream stream = OpenRead(Path.Combine(directory, "manifest.json"));
        if (stream.Length is 0 or > ContentPackageManifestCodec.MaximumManifestBytes) throw new InvalidDataException("ContentPackage.InvalidPackageManifest");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return ContentPackageManifestCodec.Deserialize(bytes, hash, Kind);
    }

    private static FileStream OpenRead(string path)
    {
        ContentPackagePathPolicy.ValidateNoReparsePoints(path);
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>Hashes <paramref name="input"/> chunk by chunk, optionally copying each chunk to <paramref name="output"/>, and reports every chunk.</summary>
    private static async Task<string> HashAsync(FileStream input, FileStream? output, byte[] buffer, ImportProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            sha256.AppendData(buffer, 0, count);
            if (output is not null) await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            reporter.Advance(count);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    private static async Task WriteManifestAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private SourceInventory Inventory(string rootDirectory)
    {
        List<string> directories = [];
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(rootDirectory);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out string? directory))
        {
            ContentPackagePathPolicy.ValidateNoReparsePoints(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative = Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
                _ = paths.Resolve(rootDirectory, relative);
                if (!names.Add(relative)) throw new InvalidDataException("ContentPackage.DuplicatePackagePath");
                if (Directory.Exists(path)) { directories.Add(relative); pending.Push(path); }
                else files.Add(relative);
                if (files.Count > Kind.MaximumFiles || directories.Count > Kind.MaximumDirectories)
                    throw new InvalidDataException("ContentPackage.PackageTooManyFiles");
            }
        }
        return new(directories.Order(StringComparer.Ordinal).ToArray(), files.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool IsWithin(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SameInventory(SourceInventory left, SourceInventory right) =>
        left.Directories.Order(StringComparer.Ordinal).SequenceEqual(right.Directories.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
        left.Files.Order(StringComparer.Ordinal).SequenceEqual(right.Files.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static void DeleteOwnedTree(string directory)
    {
        ContentPackagePathPolicy.ValidateNoReparsePoints(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            ContentPackagePathPolicy.ValidateNoReparsePoints(path);
            if (Directory.Exists(path)) DeleteOwnedTree(path);
            else File.Delete(path);
        }
        Directory.Delete(directory, recursive: false);
    }

    private sealed record SourceInventory(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);

    /// <summary>
    /// Turns the copy, source re-hash and copy verification chunks into one completion. Reports are throttled to a whole-percent change or
    /// <see cref="ReportIntervalMilliseconds"/>, whichever comes first, and are clamped so a source that grows after the
    /// inventory cannot report more than the totals.
    /// </summary>
    private sealed class ImportProgressReporter(IProgress<ContentPackageImportProgress>? progress, long totalBytes, int totalFiles)
    {
        private const int PassCount = 3;
        private const long ReportIntervalMilliseconds = 100;
        private long processedBytes;
        private int completedFiles;
        private long lastPercent = -1;
        private long lastReportTime;

        /// <summary>Counts bytes of any pass; each file is read three times: to copy it, to re-hash the source and to verify the copy.</summary>
        public void Advance(int bytes)
        {
            processedBytes += bytes;
            Report();
        }

        public void CompleteFile()
        {
            completedFiles++;
            Report();
        }

        /// <summary>Reports the totals once every file is verified, even when the last throttled report was lower.</summary>
        public void Complete() => progress?.Report(new(totalBytes, totalBytes, totalFiles, totalFiles));

        private void Report()
        {
            if (progress is null) return;
            long completedBytes = Math.Min(processedBytes / PassCount, totalBytes);
            int files = Math.Min(completedFiles, totalFiles);
            long percent = totalBytes > 0 ? completedBytes * 100 / totalBytes : files * 100L / totalFiles;
            long now = Environment.TickCount64;
            if (percent == lastPercent && now - lastReportTime < ReportIntervalMilliseconds) return;
            lastPercent = percent;
            lastReportTime = now;
            progress.Report(new(completedBytes, totalBytes, files, totalFiles));
        }
    }
}
