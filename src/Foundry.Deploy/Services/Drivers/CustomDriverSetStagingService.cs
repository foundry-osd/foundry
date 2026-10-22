// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Services.Drivers;

/// <summary>Copies a custom driver set from the boot media and verifies each file while it is copied, so only verified bytes reach the target.</summary>
public sealed class CustomDriverSetStagingService(ILogger<CustomDriverSetStagingService>? logger = null) : ICustomDriverSetStagingService
{
    private const int BufferSize = 81920;
    private static readonly ContentPackagePathPolicy PathPolicy = new(ContentPackageKind.Drivers);
    private readonly ILogger _logger = logger ?? NullLogger<CustomDriverSetStagingService>.Instance;

    /// <summary>Resolves the media folder that holds the files of <paramref name="set"/>.</summary>
    public static string ResolveSourceRoot(CustomDriverSetOption set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return Path.Combine(set.MediaRoot, ContentPackageKind.Drivers.MediaRoot.Replace('/', Path.DirectorySeparatorChar), "Packages", set.ContentHash, "files");
    }

    /// <inheritdoc />
    public async Task<string> StageAsync(CustomDriverSetOption set, string destinationRoot, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        _logger.LogInformation(
            "Custom driver set staging started. SetName={SetName}, ContentHash={ContentHash}, FileCount={FileCount}, InfCount={InfCount}",
            set.DisplayName, set.ContentHash, set.FileCount, set.InfCount);

        string? destination = null;
        try
        {
            ValidateIdentity(set);
            string sourceRoot = ResolveSourceRoot(set);
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("The custom driver set is no longer on the boot media.");

            ContentPackagePathPolicy.ValidateNoReparsePoints(destinationRoot);
            string candidate = Path.Combine(Path.GetFullPath(destinationRoot), set.ContentHash);
            // A folder left by an interrupted attempt is never trusted: the set is copied again from verified media.
            if (Directory.Exists(candidate)) Directory.Delete(candidate, recursive: true);
            destination = candidate;
            Directory.CreateDirectory(destination);

            foreach (string directory in set.Manifest.Directories)
                Directory.CreateDirectory(PathPolicy.Resolve(destination, directory));

            long copied = 0;
            foreach (ContentPackageFile file in set.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string source = PathPolicy.Resolve(sourceRoot, file.RelativePath);
                string target = PathPolicy.Resolve(destination, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyVerifiedAsync(source, target, file, cancellationToken).ConfigureAwait(false);
                copied += file.Length;
                if (set.Length > 0) progress?.Report(copied * 100d / set.Length);
            }

            _logger.LogInformation(
                "Custom driver set staging succeeded. SetName={SetName}, ContentHash={ContentHash}, FileCount={FileCount}, InfCount={InfCount}, Destination={Destination}",
                set.DisplayName, set.ContentHash, set.FileCount, set.InfCount, destination);
            return destination;
        }
        catch (OperationCanceledException)
        {
            RemovePartialSet(destination);
            _logger.LogWarning(
                "Custom driver set staging cancelled. SetName={SetName}, ContentHash={ContentHash}, FileCount={FileCount}, InfCount={InfCount}",
                set.DisplayName, set.ContentHash, set.FileCount, set.InfCount);
            throw;
        }
        catch (Exception exception)
        {
            RemovePartialSet(destination);
            _logger.LogError(exception,
                "Custom driver set staging failed. SetName={SetName}, ContentHash={ContentHash}, FileCount={FileCount}, InfCount={InfCount}",
                set.DisplayName, set.ContentHash, set.FileCount, set.InfCount);
            throw;
        }
    }

    /// <summary>Rejects a set whose bound identity no longer matches its verified manifest before any byte is copied.</summary>
    private static void ValidateIdentity(CustomDriverSetOption set)
    {
        if (!ContentPackagePathPolicy.IsValidHash(set.ContentHash) || set.Manifest.SchemaVersion != 1 ||
            set.Manifest.Files.Count != set.FileCount || set.Manifest.Files.Sum(file => file.Length) != set.Length)
            throw new InvalidDataException("The custom driver set does not match its verified manifest.");
    }

    /// <summary>Copies one file and hashes the same bytes it writes, so a file changed on the media cannot pass verification.</summary>
    private static async Task CopyVerifiedAsync(string source, string target, ContentPackageFile file, CancellationToken cancellationToken)
    {
        if (file.Length < 0 || !ContentPackagePathPolicy.IsValidHash(file.Sha256))
            throw new InvalidDataException("The custom driver set manifest holds invalid file metadata.");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != file.Length) throw new InvalidDataException("A custom driver file has an unexpected length.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferSize];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > file.Length) throw new InvalidDataException("A custom driver file has an unexpected length.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (total != file.Length) throw new InvalidDataException("A custom driver file has an unexpected length.");
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A custom driver file failed SHA-256 verification.");
    }

    private void RemovePartialSet(string? destination)
    {
        if (destination is null || !Directory.Exists(destination)) return;
        try { Directory.Delete(destination, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The deployment already fails; a leftover folder on the target is never reused, since staging always starts clean.
            _logger.LogWarning(exception, "Partially staged custom driver set could not be removed. Destination={Destination}", destination);
        }
    }
}
