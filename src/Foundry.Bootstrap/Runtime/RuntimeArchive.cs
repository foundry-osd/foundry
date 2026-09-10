// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO.Compression;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Validates ZIP entry destinations before running the provisioned architecture-specific extractor.</summary>
internal static class RuntimeArchive
{
    /// <summary>Extracts into staging; cancellation may terminate this short-lived tool, never an application process.</summary>
    internal static async Task ExtractAsync(string tool, string archive, string destination, CancellationToken cancellationToken)
    {
        ValidateEntries(archive, destination);
        if (!File.Exists(tool)) throw new FileNotFoundException("7-Zip was not provisioned in this image.", tool);
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "x", "-y", "-o" + destination, archive }) start.ArgumentList.Add(argument);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("7-Zip could not be started.");
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidDataException($"7-Zip extraction failed with exit code {process.ExitCode}.");
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
            throw;
        }
    }

    private static void ValidateEntries(string archive, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(root, relative));
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || relative.Contains(':') || unixType == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive contains an unsafe entry destination.");
        }
    }
}
