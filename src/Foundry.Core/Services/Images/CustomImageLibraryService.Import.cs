// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Configuration;
using Serilog;

namespace Foundry.Core.Services.Images;

public sealed partial class CustomImageLibraryService
{
    /// <summary>Streams imports to a private pending directory and publishes only fully inspected content.</summary>
    public async Task<CustomImageReference> ImportAsync(CustomImageImportRequest request,
        IProgress<CustomImageImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = CustomImageSettingsValidator.NormalizeDisplayName(request.DisplayName);
        cancellationToken.ThrowIfCancellationRequested();
        string pending = OwnedPath($"pending/{Guid.NewGuid():N}");
        using FileStream operationLease = PreparePendingImport(pending);
        Log.ForContext<CustomImageLibraryService>().Information("Custom image import started.");
        try
        {
            await using CustomImageImportSource source = await CustomImageImportSource.OpenAsync(request.SourcePath, cancellationToken, request.IsoImagePath).ConfigureAwait(false);
            string stagedWim = Path.Combine(pending, "image.wim");
            await using (FileStream sourceLease = OpenRead(source.ImagePath))
            {
                if (Path.GetExtension(source.ImagePath).Equals(".esd", StringComparison.OrdinalIgnoreCase))
                {
                    IReadOnlyList<CustomImageIndex> esdIndexes = await metadataReader.ReadAsync(source.ImagePath, cancellationToken).ConfigureAwait(false);
                    if (esdIndexes.Count is 0 or > CustomImageSettingsValidator.MaximumIndexes)
                        throw new InvalidDataException("The source image does not contain usable image indexes.");
                    var exportVolume = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(stagedWim))!);
                    CustomImageExportCapacityPolicy.Validate(esdIndexes, exportVolume.AvailableFreeSpace, exportVolume.DriveFormat);
                    foreach (CustomImageIndex index in esdIndexes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new("Exporting", index.Index - 1, esdIndexes.Count));
                        await CustomImageImportSource.ExportIndexAsync(source.ImagePath, stagedWim, index.Index, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await CopyAsync(sourceLease, stagedWim, progress, cancellationToken).ConfigureAwait(false);
                }
            }

            IReadOnlyList<CustomImageIndex> indexes;
            string imageHash;
            long length;
            await using (FileStream stagedLease = OpenRead(stagedWim))
            {
                progress?.Report(new("Inspecting", 0, stagedLease.Length));
                indexes = await metadataReader.ReadAsync(stagedWim, cancellationToken).ConfigureAwait(false);
                length = stagedLease.Length;
                imageHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stagedLease, cancellationToken).ConfigureAwait(false));
            }
            IReadOnlyList<CustomImageSourceFile> sourceFiles = [];
            string? bundleHash = null;
            if (request.IncludeOptionalFeatureSources && source.SourceDirectoryPath is not null)
            {
                sourceFiles = await CopySourcesAsync(source.SourceDirectoryPath, Path.Combine(pending, "sxs"), progress, cancellationToken).ConfigureAwait(false);
                if (sourceFiles.Count > 0)
                {
                    bundleHash = ComputeBundleHash(sourceFiles);
                    await File.WriteAllBytesAsync(Path.Combine(pending, "files.json"),
                        JsonSerializer.SerializeToUtf8Bytes(sourceFiles, ConfigurationJsonDefaults.SerializerOptions), cancellationToken).ConfigureAwait(false);
                }
            }
            var reference = new CustomImageReference
            {
                Id = Guid.NewGuid().ToString("N"),
                ContentHash = imageHash,
                DisplayName = name,
                Length = length,
                Indexes = indexes,
                SourceBundleHash = bundleHash
            };
            if (!CustomImageSettingsValidator.IsValidReference(reference))
                throw new InvalidDataException("The source does not contain valid readable image metadata.");
            cancellationToken.ThrowIfCancellationRequested();
            using (FileStream libraryLock = AcquireLibraryLock())
            {
                IReadOnlyList<CustomImageReference> current = await ListAsync(cancellationToken).ConfigureAwait(false);
                CustomImageReference? existing = current.FirstOrDefault(image =>
                    image.ContentHash == imageHash && image.SourceBundleHash == bundleHash);
                if (existing is not null)
                {
                    reference = reference with { Id = existing.Id };
                }
                if (existing is null && current.Count >= CustomImageSettingsValidator.MaximumImages)
                    throw new InvalidDataException("The custom image library has reached its image limit.");
                string contentDirectory = OwnedPath($"content/{imageHash}");
                Directory.CreateDirectory(contentDirectory);
                string destination = Path.Combine(contentDirectory, "image.wim");
                if (File.Exists(destination))
                {
                    try
                    {
                        await using FileStream existingImage = OpenRead(destination);
                        await VerifyAsync(existingImage, length, imageHash, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException)
                    {
                        File.Move(stagedWim, destination, overwrite: true);
                    }
                }
                else File.Move(stagedWim, destination);

                if (bundleHash is not null)
                {
                    string bundleDirectory = OwnedPath($"sources/{bundleHash}");
                    if (!Directory.Exists(bundleDirectory))
                    {
                        string stagedBundle = Path.Combine(pending, "bundle");
                        Directory.CreateDirectory(stagedBundle);
                        Directory.Move(Path.Combine(pending, "sxs"), Path.Combine(stagedBundle, "sxs"));
                        File.Move(Path.Combine(pending, "files.json"), Path.Combine(stagedBundle, "files.json"));
                        Directory.CreateDirectory(Path.GetDirectoryName(bundleDirectory)!);
                        Directory.Move(stagedBundle, bundleDirectory);
                    }
                    else
                    {
                        foreach (CustomImageSourceFile file in sourceFiles)
                        {
                            string target = CustomImagePathPolicy.ResolveRelativePath(bundleDirectory, "sxs/" + file.RelativePath);
                            bool valid = false;
                            if (File.Exists(target))
                            {
                                try
                                {
                                    await using FileStream input = OpenRead(target);
                                    await VerifyAsync(input, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false);
                                    valid = true;
                                }
                                catch (InvalidDataException) { }
                            }
                            if (valid) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            File.Move(CustomImagePathPolicy.ResolveRelativePath(pending, "sxs/" + file.RelativePath), target, overwrite: true);
                        }
                        File.Move(Path.Combine(pending, "files.json"), Path.Combine(bundleDirectory, "files.json"), overwrite: true);
                    }
                }
                await WriteIndexAsync(current.Where(image => image.Id != reference.Id).Append(reference).ToArray(), cancellationToken).ConfigureAwait(false);
            }
            progress?.Report(new("Completed", length, length));
            Log.ForContext<CustomImageLibraryService>().Information("Custom image import completed. IndexCount={IndexCount}, ImageBytes={ImageBytes}", indexes.Count, length);
            return reference;
        }
        catch (OperationCanceledException)
        {
            Log.ForContext<CustomImageLibraryService>().Information("Custom image import canceled.");
            throw;
        }
        catch (Exception exception)
        {
            Log.ForContext<CustomImageLibraryService>().Error(exception, "Custom image import failed.");
            throw;
        }
        finally
        {
            operationLease.Dispose();
            try { DeleteOwnedDirectory(pending, rootDirectory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.ForContext<CustomImageLibraryService>().Warning(exception, "Custom image pending content cleanup failed.");
            }
        }
    }

    private FileStream PreparePendingImport(string pending)
    {
        using FileStream libraryLock = AcquireLibraryLock();
        string pendingRoot = OwnedPath("pending");
        Directory.CreateDirectory(pendingRoot);
        foreach (string directory in Directory.EnumerateDirectories(pendingRoot).Take(256))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                CustomImagePathPolicy.ValidateNoReparsePoints(directory);
                using (new FileStream(Path.Combine(directory, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                DeleteOwnedDirectory(directory, rootDirectory);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.ForContext<CustomImageLibraryService>().Debug("A pending image import remains in use or cannot be cleaned up.");
            }
        }
        Directory.CreateDirectory(pending);
        return new FileStream(Path.Combine(pending, ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    private static async Task CopyAsync(FileStream input, string destination, IProgress<CustomImageImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        long available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
        if (input.Length > available) throw new IOException("There is insufficient space to import the image source.");
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024];
        long copied = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied += count;
            progress?.Report(new("Copying", copied, input.Length));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task<IReadOnlyList<CustomImageSourceFile>> CopySourcesAsync(string source, string destination,
        IProgress<CustomImageImportProgress>? progress, CancellationToken cancellationToken)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(source);
        var results = new List<CustomImageSourceFile>();
        var pending = new Stack<string>();
        pending.Push(source);
        int entries = 0;
        while (pending.TryPop(out string? directory))
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(path);
                if (++entries > MaximumSourceFiles) throw new InvalidDataException("The optional-feature source contains too many files.");
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                string relative = Path.GetRelativePath(source, path).Replace('\\', '/');
                string target = CustomImagePathPolicy.ResolveRelativePath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using FileStream input = OpenRead(path);
                await CopyAsync(input, target, progress, cancellationToken).ConfigureAwait(false);
                input.Position = 0;
                results.Add(new()
                {
                    RelativePath = relative,
                    Length = input.Length,
                    ContentHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false))
                });
            }
        }
        return results.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }
}
