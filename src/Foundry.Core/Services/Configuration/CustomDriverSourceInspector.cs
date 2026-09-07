// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>Checks custom driver sources without following reparse entries or blocking the caller's thread.</summary>
/// <remarks>One scan remains owned until enumeration exits, even when cancellation cannot interrupt native IO.</remarks>
public sealed class CustomDriverSourceInspector
{
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private readonly Func<string, FileAttributes> attributes;
    private readonly Func<string, IEnumerable<string>> entries;
    public CustomDriverSourceInspector() : this(File.GetAttributes, Directory.EnumerateFileSystemEntries) { }
    internal CustomDriverSourceInspector(Func<string, FileAttributes> attributes, Func<string, IEnumerable<string>> entries)
    {
        this.attributes = attributes;
        this.entries = entries;
    }
    /// <summary>Finds the first INF or reports a missing, inaccessible or empty source; cancellation never abandons an active scan.</summary>
    public async Task<CustomDriverSourceInspection> InspectAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = path?.Trim() ?? "";
        if (normalized.Length == 0) return new(normalized, CustomDriverSourceState.Empty, null);
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Inspect(normalized, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally { scanGate.Release(); }
    }

    private CustomDriverSourceInspection Inspect(string path, CancellationToken cancellationToken)
    {
        bool rootChecked = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes root = attributes(path);
            if ((root & FileAttributes.ReparsePoint) != 0)
                return new(path, CustomDriverSourceState.Inaccessible, "driver_source_reparse");
            if ((root & FileAttributes.Directory) == 0)
                return new(path, CustomDriverSourceState.Missing, "driver_source_missing");
            rootChecked = true;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.TryPop(out string? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (string entry in entries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes item = attributes(entry);
                    if ((item & FileAttributes.ReparsePoint) != 0) continue;
                    if ((item & FileAttributes.Directory) != 0) pending.Push(entry);
                    else if (string.Equals(Path.GetExtension(entry), ".inf", StringComparison.OrdinalIgnoreCase))
                        return new(path, CustomDriverSourceState.Ready, null);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(path, CustomDriverSourceState.NoDrivers, "driver_source_no_inf");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool missing = !rootChecked && error is FileNotFoundException or DirectoryNotFoundException;
            return new(path, missing ? CustomDriverSourceState.Missing : CustomDriverSourceState.Inaccessible,
                missing ? "driver_source_missing" : "driver_source_inaccessible");
        }
    }
}
