// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Images;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Checks and holds the authenticated runtime prepared by Bootstrap until target staging.</summary>
internal static class PostInstallRuntimeSource
{
    internal static async Task<PreOobePreparedContent> AcquireAsync(string? executablePath, string expectedRid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath) ||
            !Path.GetFileName(executablePath).Equals("Foundry.PostInstall.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bootstrap did not provide a PostInstall runtime executable.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;
        string manifestPath = Path.Combine(directory, PostInstallRuntimeManifest.FileName);
        CustomImageSourceLease.EnsureRegularPath(manifestPath);
        var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        PreOobePreparedContent? prepared = null;
        try
        {
            if (manifestStream.Length is <= 0 or > 65536)
                throw new InvalidDataException("PostInstall runtime metadata exceeds its size limit.");
            PostInstallRuntimeManifest manifest;
            try
            {
                manifest = await JsonSerializer.DeserializeAsync<PostInstallRuntimeManifest>(manifestStream,
                    ConfigurationJsonDefaults.SerializerOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("PostInstall runtime metadata is missing.");
            }
            catch (JsonException exception) { throw new InvalidDataException("PostInstall runtime metadata is invalid.", exception); }
            Validate(manifest, expectedRid);
            prepared = new() { RuntimeIdentifier = expectedRid, RuntimeDirectory = directory, RuntimeManifest = manifest };
            prepared.Files.Add((manifestPath, manifestStream));
            foreach (var file in manifest.Files)
            {
                string path = PreOobePackagePathPolicy.Resolve(directory, file.RelativePath);
                prepared.Files.Add((path, await PreOobeContentResolver.OpenVerifiedAsync(path, file.Length, file.Sha256, cancellationToken).ConfigureAwait(false)));
            }
            return prepared;
        }
        catch
        {
            if (prepared is null) manifestStream.Dispose();
            else prepared.Dispose();
            throw;
        }
    }

    private static void Validate(PostInstallRuntimeManifest manifest, string expectedRid)
    {
        if (manifest.SchemaVersion != 1 || manifest.ContractVersion != 1 ||
            expectedRid is not ("win-x64" or "win-arm64") || manifest.RuntimeIdentifier != expectedRid ||
            manifest.Files is null || manifest.Files.Count is < 2 or > 1024)
            throw new InvalidDataException("PostInstall runtime is incompatible with the selected Windows image or deployment contract.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null) throw new InvalidDataException("PostInstall runtime file metadata is invalid.");
            PreOobePackagePathPolicy.ValidateRelativePath(file.RelativePath);
            if (!paths.Add(file.RelativePath) || file.RelativePath.Equals(PostInstallRuntimeManifest.FileName, StringComparison.OrdinalIgnoreCase) ||
                file.Length is <= 0 or > 512L * 1024 * 1024 || !PreOobePackagePathPolicy.IsValidHash(file.Sha256))
                throw new InvalidDataException("PostInstall runtime file metadata is invalid.");
            total += file.Length;
            if (total > 512L * 1024 * 1024) throw new InvalidDataException("PostInstall runtime exceeds its size limit.");
        }
        if (!paths.Contains("Foundry.PostInstall.exe") || !paths.Contains("Launch.cmd"))
            throw new InvalidDataException("PostInstall runtime is missing required launch files.");
    }

    internal static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + count);
            if (total > expected) throw new InvalidDataException("Content exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (total != expected) throw new InvalidDataException("Content is incomplete.");
    }
}
