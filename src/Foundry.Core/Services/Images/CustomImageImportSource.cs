// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Foundry.Core.Services.Images;

/// <summary>Owns only ISO mounts created by this operation and releases them after image copying completes.</summary>
internal sealed class CustomImageImportSource : IAsyncDisposable
{
    private readonly string? isoPath;
    private readonly FileStream? isoLease;
    private CustomImageImportSource(string imagePath, string? sourceDirectoryPath, string? isoPath, FileStream? isoLease)
    {
        ImagePath = imagePath;
        SourceDirectoryPath = sourceDirectoryPath;
        this.isoPath = isoPath;
        this.isoLease = isoLease;
    }

    public string ImagePath { get; }
    public string? SourceDirectoryPath { get; }

    public static async Task<CustomImageImportSource> OpenAsync(string path, CancellationToken cancellationToken, string? isoImagePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        CustomImagePathPolicy.ValidateNoReparsePoints(fullPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The import source is unavailable.", fullPath);
        string extension = Path.GetExtension(fullPath);
        if (extension.Equals(".wim", StringComparison.OrdinalIgnoreCase))
            return new(fullPath, null, null, null);
        if (!extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a WIM file or an ISO containing sources/install.wim or sources/install.esd.");

        cancellationToken.ThrowIfCancellationRequested();
        var lease = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        bool owned = false;
        try
        {
            const string script = """
                $ErrorActionPreference = 'Stop'
                $image = Get-DiskImage -ImagePath $env:FOUNDRY_IMPORT_ISO
                $owned = -not $image.Attached
                try {
                    if ($owned) { $image = Mount-DiskImage -ImagePath $env:FOUNDRY_IMPORT_ISO -Access ReadOnly -PassThru }
                    $volume = $image | Get-Volume | Where-Object { $_.DriveLetter } | Select-Object -First 1
                    if (-not $volume) { throw 'The ISO has no accessible volume.' }
                    @{ Root = "$($volume.DriveLetter):\"; Owned = $owned } | ConvertTo-Json -Compress
                } catch {
                    if ($owned) { Dismount-DiskImage -ImagePath $env:FOUNDRY_IMPORT_ISO -ErrorAction SilentlyContinue }
                    throw
                }
                """;
            string json = await RunPowerShellAsync(script, fullPath).ConfigureAwait(false);
            using JsonDocument result = JsonDocument.Parse(json);
            owned = result.RootElement.GetProperty("Owned").GetBoolean();
            string root = result.RootElement.GetProperty("Root").GetString() ?? throw new InvalidDataException("The ISO volume is unavailable.");
            cancellationToken.ThrowIfCancellationRequested();
            string image = ResolveInstallationPath(root, isoImagePath);
            string sxs = CustomImagePathPolicy.ResolveRelativePath(root, "sources/sxs");
            return new(image, Directory.Exists(sxs) ? sxs : null, owned ? fullPath : null, lease);
        }
        catch
        {
            if (owned) await UnmountAsync(fullPath).ConfigureAwait(false);
            lease.Dispose();
            throw;
        }
    }

    internal static string ResolveInstallationPath(string root, string? selected)
    {
        string[] candidates = new[] { "sources/install.wim", "sources/install.esd" }
            .Where(relative => File.Exists(CustomImagePathPolicy.ResolveRelativePath(root, relative))).ToArray();
        if (selected is not null)
        {
            string? match = candidates.FirstOrDefault(candidate => candidate.Equals(selected.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (match is null) throw new InvalidDataException("The selected ISO installation image is unavailable.");
            return CustomImagePathPolicy.ResolveRelativePath(root, match);
        }
        if (candidates.Length > 1) throw new CustomImageSourceChoiceRequiredException(candidates);
        if (candidates.Length == 0) throw new InvalidDataException("The ISO does not contain sources/install.wim or sources/install.esd. Split SWM images are not supported.");
        return CustomImagePathPolicy.ResolveRelativePath(root, candidates[0]);
    }

    public async ValueTask DisposeAsync()
    {
        try { if (isoPath is not null) await UnmountAsync(isoPath).ConfigureAwait(false); }
        finally { isoLease?.Dispose(); }
    }

    public static async Task ExportIndexAsync(string source, string destination, int index, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/English", "/Export-Image", "/SourceImageFile:" + source,
            "/SourceIndex:" + index.ToString(CultureInfo.InvariantCulture), "/DestinationImageFile:" + destination, "/Compress:max", "/CheckIntegrity" })
            start.ArgumentList.Add(argument);
        await RunAsync(start, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UnmountAsync(string path)
    {
        try
        {
            await RunPowerShellAsync("$ErrorActionPreference = 'Stop'; Dismount-DiskImage -ImagePath $env:FOUNDRY_IMPORT_ISO", path).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Log.ForContext<CustomImageImportSource>().Warning(exception, "The imported ISO could not be detached.");
        }
    }

    private static Task<string> RunPowerShellAsync(string script, string path)
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment["FOUNDRY_IMPORT_ISO"] = path;
        // Mounting is completed before honoring cancellation so ownership is known and cleanup can detach safely.
        return RunAsync(start, CancellationToken.None);
    }

    private static async Task<string> RunAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(start) ?? throw new IOException("The image import process could not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            throw;
        }
        await Task.WhenAll(output, error).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException($"Image import process failed with exit code {process.ExitCode}: {error.Result.Trim()}");
        return output.Result.Trim();
    }
}
