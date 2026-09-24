// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Publishes top-level log snapshots and prunes only unchanged copies owned by this source.</summary>
public static class DiagnosticLogSnapshot
{
    /// <summary>Copies only top-level matching files; failures leave prior manifests and unrelated evidence intact.</summary>
    public static async Task<int> CopyAsync(string sourceDirectory, string destinationDirectory,
        string searchPattern, CancellationToken cancellationToken = default)
    {
        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        string targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        if (sourceRoot.Equals(targetRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(sourceRoot)) return 0;

        Directory.CreateDirectory(targetRoot);
        RejectReparsePoint(sourceRoot);
        RejectReparsePoint(targetRoot);
        string owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceRoot.ToUpperInvariant() + "|" + searchPattern)));
        string manifestPath = Path.Combine(targetRoot, $".snapshot-{owner}.json");
        string lockPath = Path.Combine(targetRoot, ".snapshot.lease");
        if (File.Exists(lockPath)) RejectReparsePoint(lockPath);
        // A busy target is a recoverable snapshot failure; callers retain their source and retry later.
        await using FileStream lease = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Dictionary<string, string> previous = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(manifestPath))
        {
            RejectReparsePoint(manifestPath);
            try
            {
                previous = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false)) ?? previous;
            }
            catch (JsonException)
            {
                // Unreadable ownership metadata never authorizes pruning.
            }
        }

        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, searchPattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(sourcePath);
            string name = Path.GetFileName(sourcePath);
            if (name.StartsWith(".snapshot", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Path.Combine(targetRoot, name);
            if (File.Exists(target)) RejectReparsePoint(target);
            string temporary = target + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous))
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                // Hash the stable copied bytes, not a source log that can keep growing.
                current[name] = await HashAsync(temporary, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }
        }

        foreach ((string name, string hash) in previous)
        {
            // Manifests may own only leaf files. Modified files have become independent evidence.
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || current.ContainsKey(name)) continue;
            string obsolete = Path.Combine(targetRoot, name);
            if (!File.Exists(obsolete)) continue;
            RejectReparsePoint(obsolete);
            if (string.Equals(hash, await HashAsync(obsolete, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal))
                File.Delete(obsolete);
        }

        string manifestTemporary = manifestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(manifestTemporary, JsonSerializer.Serialize(current), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(manifestTemporary, manifestPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporary(manifestTemporary);
        }
        return current.Count;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Diagnostic snapshots cannot follow reparse points.");
    }

    private static void TryDeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
