// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;

namespace Foundry.Core.Services.WinPe;

/// <summary>Keeps external WIMs off the USB BOOT tree and places boot files first in large UDF images.</summary>
internal static class WinPeCustomImageIsoMastering
{
    internal static string ResolveOscdimg(WinPeToolPaths tools)
    {
        if (!string.IsNullOrWhiteSpace(tools.OscdimgPath)) return tools.OscdimgPath;
        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        string? adk = Path.GetDirectoryName(Path.GetDirectoryName(tools.MakeWinPeMediaPath));
        string[] candidates =
        [
            Path.Combine(tools.KitsRootPath, "Assessment and Deployment Kit", "Deployment Tools", architecture, "Oscdimg", "oscdimg.exe"),
            Path.Combine(adk ?? string.Empty, "Deployment Tools", architecture, "Oscdimg", "oscdimg.exe")
        ];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("The ADK Oscdimg tool is required to create media containing custom images.");
    }

    internal static async Task PrepareAsync(WinPeBuildArtifact artifact, WinPeCustomImageMediaLease package,
        string staging, string preparedOutput, string requestedOutput, CancellationToken cancellationToken)
    {
        long bootBytes = CountBytes(artifact.MediaDirectoryPath);
        long payloadBytes = checked(bootBytes + package.TotalBytes + WinPeCustomImageMediaService.DataReserveBytes);
        foreach ((string volume, long required) in GetSpaceRequirements(staging, preparedOutput, requestedOutput, payloadBytes))
            RequireSpace(volume, required);
        foreach (string outputPath in new[] { preparedOutput, requestedOutput })
        {
            var volume = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outputPath))!);
            if (volume.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase) && payloadBytes > uint.MaxValue)
                throw new IOException("The custom-image ISO exceeds the FAT32 output file-size limit.");
        }
        await CopyTreeAsync(artifact.MediaDirectoryPath, Path.Combine(staging, "media"), cancellationToken).ConfigureAwait(false);
        await CopyTreeAsync(Path.Combine(artifact.WorkingDirectoryPath, "bootbins"), Path.Combine(staging, "bootbins"), cancellationToken).ConfigureAwait(false);
        await new WinPeCustomImageMediaService().PublishAsync(package, Path.Combine(staging, "media"), cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyDictionary<string, long> GetSpaceRequirements(string staging, string preparedOutput,
        string requestedOutput, long payloadBytes)
    {
        var requirements = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Add(staging);
        Add(preparedOutput);
        // Finalization copies whenever directories differ, even on the same volume, so the old
        // output and both pending ISO copies can coexist until the atomic replacement succeeds.
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(preparedOutput)),
            Path.GetDirectoryName(Path.GetFullPath(requestedOutput)), StringComparison.OrdinalIgnoreCase)) Add(requestedOutput);
        return requirements;

        void Add(string path)
        {
            string volume = Path.GetPathRoot(Path.GetFullPath(path))!;
            requirements[volume] = checked(requirements.GetValueOrDefault(volume) + payloadBytes);
        }
    }

    internal static string CreateArguments(string workspace, string output, bool useBootEx)
    {
        string bins = Path.Combine(workspace, "bootbins");
        string efi = Path.Combine(bins, useBootEx ? "efisys_EX.bin" : "efisys.bin");
        if (!File.Exists(efi)) throw new FileNotFoundException("The requested EFI boot image is unavailable.", efi);
        string bios = Path.Combine(bins, "etfsboot.com");
        string bootData = File.Exists(bios)
            ? $"2#p0,e,b{WinPeProcessRunner.Quote(bios)}#pEF,e,b{WinPeProcessRunner.Quote(efi)}"
            : $"1#pEF,e,b{WinPeProcessRunner.Quote(efi)}";
        string media = Path.Combine(workspace, "media");
        var ordered = new List<string>();
        foreach (string directory in new[] { "boot", "EFI" })
        {
            string path = Path.Combine(media, directory);
            if (Directory.Exists(path))
                ordered.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(media, file)).Order(StringComparer.OrdinalIgnoreCase));
        }
        foreach (string name in new[] { "bootmgr", "bootmgr.efi" })
            if (File.Exists(Path.Combine(media, name))) ordered.Add(name);
        ordered.Add(Path.Combine("sources", "boot.wim"));
        if (ordered.Any(path => path.Any(character => character > 127)))
            throw new InvalidDataException("Boot-order paths must be representable in the ADK ANSI order file.");
        string orderFile = Path.Combine(workspace, "boot-order.txt");
        File.WriteAllText(orderFile, string.Join("\r\n", ordered) + "\r\n", Encoding.ASCII);
        return $"-bootdata:{bootData} -m -u2 -udfver102 -yo{WinPeProcessRunner.Quote(orderFile)} {WinPeProcessRunner.Quote(media)} {WinPeProcessRunner.Quote(output)}";
    }

    private static void RequireSpace(string path, long bytes)
    {
        if (WinPeCustomImageMediaService.GetAvailableBytes(path) < bytes)
            throw new IOException("Insufficient free space for custom-image ISO staging and atomic output publication.");
    }

    private static long CountBytes(string root)
    {
        WinPeCustomImageMediaService.EnsureNoReparsePoints(root);
        long bytes = 0;
        foreach (FileSystemInfo entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("ISO staging does not follow reparse points.");
            bytes = checked(bytes + (entry is FileInfo file ? file.Length : CountBytes(entry.FullName)));
        }
        return bytes;
    }

    private static async Task CopyTreeAsync(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WinPeCustomImageMediaService.EnsureNoReparsePoints(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        foreach (FileSystemInfo entry in new DirectoryInfo(sourceRoot).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("ISO staging does not follow reparse points.");
            string destination = Path.Combine(destinationRoot, entry.Name);
            if (entry is DirectoryInfo) await CopyTreeAsync(entry.FullName, destination, cancellationToken).ConfigureAwait(false);
            else
            {
                await using var input = new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
