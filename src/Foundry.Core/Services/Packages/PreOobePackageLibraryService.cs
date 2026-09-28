// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Packages;

/// <summary>Imports owned immutable package snapshots separately from portable deployment profiles.</summary>
public sealed class PreOobePackageLibraryService
{
    private readonly string root;

    public PreOobePackageLibraryService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        root = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Performs lightweight readiness checks; consumers must acquire a fully verified lease before use.</summary>
    public bool IsAvailable(PreOobePackageReference reference)
    {
        if (!PreOobeConfigurationValidator.IsValidReference(reference)) return false;
        try
        {
            string directory = ContentPath(reference.ContentHash);
            PreOobePackageManifest manifest = ReadManifest(directory, reference.ContentHash);
            return manifest.Files.Count == reference.FileCount && manifest.Files.Sum(file => file.Length) == reference.Length &&
                manifest.Files.All(file => File.Exists(PreOobePackagePathPolicy.Resolve(Path.Combine(directory, "files"), file.RelativePath)) &&
                    new FileInfo(PreOobePackagePathPolicy.Resolve(Path.Combine(directory, "files"), file.RelativePath)).Length == file.Length);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException) { return false; }
    }

    /// <summary>Freezes a file or folder without modifying the source. Identical bytes and relative names reuse one version.</summary>
    public async Task<PreOobePackageReference> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(sourcePath);
        PreOobePackagePathPolicy.ValidateNoReparsePoints(source);
        if (IsWithin(source, root) || (Directory.Exists(source) && IsWithin(root, source)))
            throw new InvalidDataException("PreOobe.PackageSourceOverlapsLibrary");
        bool isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source)) throw new FileNotFoundException("PreOobe.PackageSourceMissing");
        string sourceRoot = isDirectory ? source : Path.GetDirectoryName(source)!;
        SourceInventory inventory = isDirectory ? Inventory(sourceRoot) : new([], [Path.GetFileName(source)]);
        if (inventory.Files.Count == 0) throw new InvalidDataException("PreOobe.EmptyPackage");
        using FileStream libraryLock = AcquireLibraryLock();
        string pending = Path.Combine(root, "pending", Guid.NewGuid().ToString("N"));
        string filesRoot = Path.Combine(pending, "files");
        Directory.CreateDirectory(filesRoot);
        List<FileStream> sourceHandles = [];
        try
        {
            foreach (string directory in inventory.Directories) Directory.CreateDirectory(PreOobePackagePathPolicy.Resolve(filesRoot, directory));
            List<PreOobePackageFile> files = [];
            foreach (string relativePath in inventory.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileStream input = OpenRead(PreOobePackagePathPolicy.Resolve(sourceRoot, relativePath));
                sourceHandles.Add(input);
                if (new DriveInfo(Path.GetPathRoot(filesRoot)!).AvailableFreeSpace < input.Length)
                    throw new IOException("PreOobe.InsufficientPackageSpace");
                string target = PreOobePackagePathPolicy.Resolve(filesRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                input.Position = 0;
                string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                await using (FileStream copied = OpenRead(target))
                {
                    if (copied.Length != input.Length || !hash.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(copied, cancellationToken).ConfigureAwait(false)), StringComparison.Ordinal))
                        throw new InvalidDataException("PreOobe.PackageSourceChanged");
                }
                files.Add(new() { RelativePath = relativePath, Length = input.Length, Sha256 = hash });
            }
            if (isDirectory && !SameInventory(inventory, Inventory(sourceRoot))) throw new InvalidDataException("PreOobe.PackageSourceChanged");
            var manifest = new PreOobePackageManifest { Directories = inventory.Directories, Files = files };
            byte[] manifestBytes = PreOobePackageManifestCodec.Serialize(manifest);
            string contentHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            string destination = ContentPath(contentHash);
            ValidateDestination(manifest, Path.Combine(destination, "files"));
            await WriteManifestAsync(Path.Combine(pending, "manifest.json"), manifestBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                try
                {
                    await using PreOobePackageLease existing = await AcquireCoreAsync(contentHash, cancellationToken).ConfigureAwait(false);
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
    public async Task<PreOobePackageLease> AcquireAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream libraryLock = AcquireLibraryLock();
        return await AcquireCoreAsync(contentHash, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes only an unreferenced, verified, unleased owned snapshot. Unknown files prevent removal.</summary>
    public async Task DeleteAsync(string contentHash, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referencedHashes);
        if (referencedHashes.Contains(contentHash, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("PreOobe.PackageInUse");
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream libraryLock = AcquireLibraryLock();
        string directory = ContentPath(contentHash);
        if (!Directory.Exists(directory)) return;
        await using (PreOobePackageLease validation = await AcquireCoreAsync(contentHash, cancellationToken, verifyContent: false).ConfigureAwait(false)) { }
        PreOobePackageManifest manifest = ReadManifest(directory, contentHash);
        List<FileStream> deleteHandles = [];
        try
        {
            foreach (PreOobePackageFile file in manifest.Files)
                deleteHandles.Add(new(PreOobePackagePathPolicy.Resolve(Path.Combine(directory, "files"), file.RelativePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            deleteHandles.Add(new(Path.Combine(directory, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FileStream handle in deleteHandles) File.Delete(handle.Name);
            foreach (FileStream handle in deleteHandles) handle.Dispose();
            foreach (string child in manifest.Directories.OrderByDescending(path => path.Length))
                Directory.Delete(PreOobePackagePathPolicy.Resolve(Path.Combine(directory, "files"), child), recursive: false);
            Directory.Delete(Path.Combine(directory, "files"), recursive: false);
            Directory.Delete(directory, recursive: false);
        }
        finally { foreach (FileStream handle in deleteHandles) handle.Dispose(); }
    }

    /// <summary>Checks actual library and target prefixes against installer path limits before copying.</summary>
    public static void ValidateDestination(PreOobePackageManifest manifest, string destinationRoot)
    {
        PreOobePackageManifestCodec.Validate(manifest);
        foreach (string path in manifest.Directories.Concat(manifest.Files.Select(file => file.RelativePath)))
            _ = PreOobePackagePathPolicy.Resolve(destinationRoot, path);
    }

    private async Task<PreOobePackageLease> AcquireCoreAsync(string hash, CancellationToken cancellationToken, bool verifyContent = true)
    {
        string directory = ContentPath(hash);
        string filesRoot = Path.Combine(directory, "files");
        List<FileStream> handles = [];
        try
        {
            FileStream metadata = OpenRead(Path.Combine(directory, "manifest.json"));
            handles.Add(metadata);
            if (metadata.Length is 0 or > PreOobePackageManifestCodec.MaximumManifestBytes) throw new InvalidDataException("PreOobe.InvalidPackageManifest");
            byte[] bytes = new byte[checked((int)metadata.Length)];
            await metadata.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            PreOobePackageManifest manifest = PreOobePackageManifestCodec.Deserialize(bytes, hash);
            ValidateDestination(manifest, filesRoot);
            SourceInventory inventory = Inventory(filesRoot);
            if (!SameInventory(new(manifest.Directories, manifest.Files.Select(file => file.RelativePath).ToArray()), inventory) ||
                Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) is not ("files" or "manifest.json")))
                throw new InvalidDataException("PreOobe.PackageContentChanged");
            List<PreOobePackageSourceFile> sources = [];
            foreach (PreOobePackageFile file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = PreOobePackagePathPolicy.Resolve(filesRoot, file.RelativePath);
                FileStream input = OpenRead(path);
                handles.Add(input);
                if (verifyContent && (input.Length != file.Length || !Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("PreOobe.PackageContentChanged");
                input.Position = 0;
                sources.Add(new(path, file.RelativePath, file.Length, file.Sha256));
            }
            return new(CreateReference(manifest, hash.ToLowerInvariant(), hash.ToLowerInvariant()), manifest, filesRoot, sources, handles);
        }
        catch { foreach (FileStream handle in handles) handle.Dispose(); throw; }
    }

    private static PreOobePackageReference CreateReference(PreOobePackageManifest manifest, string hash, string name) => new()
    {
        ContentHash = hash,
        DisplayName = name,
        Length = manifest.Files.Sum(file => file.Length),
        FileCount = manifest.Files.Count
    };

    /// <summary>Replaces a damaged known revision only when no consumer holds its content and no unmanaged entries would be removed.</summary>
    private void RepairOwnedRevision(string destination, string pending, string hash, CancellationToken cancellationToken)
    {
        PreOobePackageManifest manifest = ReadManifest(destination, hash);
        string filesRoot = Path.Combine(destination, "files");
        SourceInventory inventory = Directory.Exists(filesRoot) ? Inventory(filesRoot) : new([], []);
        var ownedFiles = manifest.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        if (inventory.Files.Any(path => !ownedFiles.Contains(path)) ||
            inventory.Directories.Any(path => !manifest.Directories.Contains(path, StringComparer.Ordinal)) ||
            Directory.EnumerateFileSystemEntries(destination).Any(path => Path.GetFileName(path) is not ("files" or "manifest.json")) ||
            File.Exists(filesRoot))
            throw new InvalidDataException("PreOobe.PackageContentChanged");

        string retired = Path.Combine(root, "pending", "retired-" + Guid.NewGuid().ToString("N"));
        List<FileStream> replacementHandles = [];
        try
        {
            // Existing leases deny write/delete sharing. Hold all replacement handles before changing any path.
            replacementHandles.Add(new(Path.Combine(destination, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            foreach (string relative in inventory.Files)
                replacementHandles.Add(new(PreOobePackagePathPolicy.Resolve(filesRoot, relative), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { foreach (FileStream handle in replacementHandles) handle.Dispose(); }

        // Windows cannot rename a directory with open children; the caller's library lock still excludes new leases.
        PreOobePackagePathPolicy.ValidateNoReparsePoints(destination);
        Directory.Move(destination, retired);
        try { Directory.Move(pending, destination); }
        catch
        {
            Directory.Move(retired, destination);
            throw;
        }

        // Delete only the recorded old entries. Unexpected additions stay in the retired directory.
        foreach (string relative in inventory.Files)
            File.Delete(PreOobePackagePathPolicy.Resolve(Path.Combine(retired, "files"), relative));
        foreach (string relative in inventory.Directories.OrderByDescending(path => path.Length))
            Directory.Delete(PreOobePackagePathPolicy.Resolve(Path.Combine(retired, "files"), relative), recursive: false);
        if (Directory.Exists(Path.Combine(retired, "files"))) Directory.Delete(Path.Combine(retired, "files"), recursive: false);
        PreOobePackagePathPolicy.ValidateNoReparsePoints(Path.Combine(retired, "manifest.json"));
        File.Delete(Path.Combine(retired, "manifest.json"));
        Directory.Delete(retired, recursive: false);
    }

    private string ContentPath(string hash)
    {
        if (!PreOobePackagePathPolicy.IsValidHash(hash)) throw new InvalidDataException("PreOobe.InvalidPackageReference");
        string path = Path.Combine(root, "content", hash.ToLowerInvariant());
        PreOobePackagePathPolicy.ValidateNoReparsePoints(path);
        return path;
    }

    private FileStream AcquireLibraryLock()
    {
        PreOobePackagePathPolicy.ValidateNoReparsePoints(root);
        Directory.CreateDirectory(root);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        foreach (SecurityIdentifier principal in new[]
        {
            identity.User ?? throw new InvalidOperationException("PreOobe.UserUnavailable"),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        })
            security.AddAccessRule(new(principal, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);
        return new(Path.Combine(root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static PreOobePackageManifest ReadManifest(string directory, string hash)
    {
        using FileStream stream = OpenRead(Path.Combine(directory, "manifest.json"));
        if (stream.Length is 0 or > PreOobePackageManifestCodec.MaximumManifestBytes) throw new InvalidDataException("PreOobe.InvalidPackageManifest");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return PreOobePackageManifestCodec.Deserialize(bytes, hash);
    }

    private static FileStream OpenRead(string path)
    {
        PreOobePackagePathPolicy.ValidateNoReparsePoints(path);
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static async Task WriteManifestAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static SourceInventory Inventory(string rootDirectory)
    {
        List<string> directories = [];
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(rootDirectory);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out string? directory))
        {
            PreOobePackagePathPolicy.ValidateNoReparsePoints(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative = Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
                _ = PreOobePackagePathPolicy.Resolve(rootDirectory, relative);
                if (!names.Add(relative)) throw new InvalidDataException("PreOobe.DuplicatePackagePath");
                if (Directory.Exists(path)) { directories.Add(relative); pending.Push(path); }
                else files.Add(relative);
                if (files.Count > PreOobePackageManifestCodec.MaximumFiles || directories.Count > PreOobePackageManifestCodec.MaximumDirectories)
                    throw new InvalidDataException("PreOobe.PackageTooManyFiles");
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
        PreOobePackagePathPolicy.ValidateNoReparsePoints(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            PreOobePackagePathPolicy.ValidateNoReparsePoints(path);
            if (Directory.Exists(path)) DeleteOwnedTree(path);
            else File.Delete(path);
        }
        Directory.Delete(directory, recursive: false);
    }

    private sealed record SourceInventory(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);
}
