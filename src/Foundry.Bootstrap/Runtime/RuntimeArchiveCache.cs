// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Runtime;

/// <summary>Publishes an optional verified update archive without replacing the original media payload.</summary>
internal static class RuntimeArchiveCache
{
    internal static async Task StoreAsync(string source, string destination, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        RejectReparsePoints(directory);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoints(directory);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            // Only the temporary file belongs to this publication; never delete the previous or original archive.
            RejectReparsePoints(directory);
            File.Delete(temporary);
        }
    }

    private static void RejectReparsePoints(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Runtime cache publication cannot traverse a reparse point.");
        }
    }
}
