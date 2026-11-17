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

/// <summary>
/// Checks and holds the PostInstall runtime that every boot media carries in <c>X:\Foundry\PostInstall</c> until target
/// staging. The boot image is read-only and nothing is downloaded or repaired: a missing or damaged runtime means the boot
/// media must be recreated.
/// </summary>
internal static class PostInstallRuntimeSource
{
    /// <summary>
    /// Verifies the manifest contract, runtime identifier and per-file length and SHA256 of the runtime in
    /// <paramref name="runtimeDirectory"/>, and locks its files until the result is disposed. The manifest release tag is
    /// informational and never compared.
    /// </summary>
    /// <param name="runtimeDirectory">Absolute directory holding <c>Foundry.PostInstall.exe</c>, <c>Launch.cmd</c> and the manifest.</param>
    /// <param name="expectedRid">Runtime identifier of the deployed Windows image.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <exception cref="PostInstallRuntimeMissingException">The runtime is missing, unreadable, incompatible or damaged.</exception>
    internal static async Task<PreOobePreparedContent> AcquireAsync(string runtimeDirectory, string expectedRid, CancellationToken cancellationToken)
    {
        try
        {
            return await AcquireCoreAsync(runtimeDirectory, expectedRid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PostInstallRuntimeMissingException(exception);
        }
    }

    private static async Task<PreOobePreparedContent> AcquireCoreAsync(string runtimeDirectory, string expectedRid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathFullyQualified(runtimeDirectory))
            throw new InvalidDataException("The PostInstall runtime directory must be an absolute path.");
        string directory = Path.GetFullPath(runtimeDirectory);
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
                string path = ContentPackagePathPolicy.PostInstallation.Resolve(directory, file.RelativePath);
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
            ContentPackagePathPolicy.PostInstallation.ValidateRelativePath(file.RelativePath);
            if (!paths.Add(file.RelativePath) || file.RelativePath.Equals(PostInstallRuntimeManifest.FileName, StringComparison.OrdinalIgnoreCase) ||
                file.Length is <= 0 or > 512L * 1024 * 1024 || !ContentPackagePathPolicy.IsValidHash(file.Sha256))
                throw new InvalidDataException("PostInstall runtime file metadata is invalid.");
            total += file.Length;
            if (total > 512L * 1024 * 1024) throw new InvalidDataException("PostInstall runtime exceeds its size limit.");
        }
        if (!paths.Contains(PostInstallRuntimeManifest.ExecutableFileName) || !paths.Contains(PostInstallRuntimeManifest.LaunchScriptFileName))
            throw new InvalidDataException("PostInstall runtime is missing required launch files.");
    }

    /// <summary>Copies exactly <paramref name="expected"/> bytes; <paramref name="onBytes"/> receives the bytes copied so far after each chunk.</summary>
    internal static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken cancellationToken, Action<long>? onBytes = null)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + count);
            if (total > expected) throw new InvalidDataException("Content exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            onBytes?.Invoke(total);
        }
        if (total != expected) throw new InvalidDataException("Content is incomplete.");
    }
}

/// <summary>
/// Reports that the PostInstall runtime of the boot media is missing, unreadable, incompatible or damaged. It is distinct
/// from invalid user post-installation content because only recreating the boot media repairs it.
/// </summary>
internal sealed class PostInstallRuntimeMissingException(Exception innerException)
    : IOException("The PostInstall runtime of this boot media is missing or damaged.", innerException);
