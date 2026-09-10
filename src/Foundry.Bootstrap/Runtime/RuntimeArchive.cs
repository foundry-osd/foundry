// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using Foundry.Utilities.IO;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Extracts validated ZIP entries into a fresh staging directory.</summary>
internal static class RuntimeArchive
{
    internal static async Task ExtractAsync(string archive, string destination, CancellationToken cancellationToken,
        Action<long, long>? progress = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        deadline.Token.ThrowIfCancellationRequested();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(archive);
        var entries = new List<(ZipArchiveEntry Entry, string Target)>();
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            deadline.Token.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(root, relative));
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (!target.StartsWith(root, StringComparison.Ordinal) || relative.Contains(':') || unixType == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive contains an unsafe entry destination.");
            entries.Add((entry, target));
            total = checked(total + entry.Length);
        }
        long extracted = 0;
        progress?.Invoke(0, total);
        foreach (var (entry, target) in entries)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(Path.GetFileName(target)))
            {
                if (entry.Length != 0) throw new InvalidDataException("Archive directory contains file data.");
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long copied = await StreamCopy.CopyAsync(input, output,
                bytes => progress?.Invoke(extracted + bytes, total), deadline.Token).ConfigureAwait(false);
            if (copied != entry.Length) throw new InvalidDataException("Extracted size differs from the archive entry size.");
            extracted += copied;
        }
        progress?.Invoke(extracted, total);
    }
}
