// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Images;
using Foundry.Deploy.Services.Deployment.Steps;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Freezes applicable external content and runtime identity before either destructive deployment branch.</summary>
public class PreOobeContentResolver(PreOobeRuntimeResolver runtimeResolver, IDeploymentStorageService storage)
{
    // IncludeAllContentForSelfExtract redirects BaseDirectory away from the executable's companion files.
    internal string DescriptorPath { get; init; } = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, PreOobeRuntimeResolver.DescriptorFileName);
    internal Func<string[]> MediaRoots { get; init; } = () => DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.RootDirectory.FullName).ToArray();

    internal static bool IsRequired(DeploymentContext request) =>
        request.PreOobe.IsEnabled && request.PreOobe.Actions.Any(action => action.IsEnabled) ||
        HasBuiltInTasks(request.AppxRemoval, request.AiComponentRemoval,
            DeploymentPlan.ResolveDriverMode(request) == Services.DriverPacks.DriverPackInstallMode.DeferredSetupComplete,
            request.Network.ProfileRoaming.IsAnyEnabled, StagePreOobeCustomizationStep.ShouldActivateWindowsOem(request));

    internal static bool HasBuiltInTasks(Models.Configuration.DeployAppxRemovalSettings appxRemoval,
        Models.Configuration.DeployAiComponentRemovalSettings aiRemoval, bool deferredDriver, bool network, bool activation) =>
        deferredDriver || network || activation || appxRemoval.IsEnabled && appxRemoval.PackageNames.Any(name => !string.IsNullOrWhiteSpace(name)) ||
        aiRemoval.IsEnabled && (aiRemoval.RemoveCopilot || aiRemoval.RemoveAiHub);

    internal static string ResolveRid(string architecture) => architecture.ToLowerInvariant() switch
    {
        "x64" or "amd64" => "win-x64",
        "arm64" => "win-arm64",
        _ => throw new InvalidDataException("Post-installation requires an x64 or ARM64 Windows image.")
    };

    internal static IEnumerable<PreOobeActionSettings> ApplicableActions(DeploymentContext request) =>
        request.PreOobe.IsEnabled ? request.PreOobe.Actions.Where(action => action.IsEnabled) : [];

    internal virtual async Task<PreOobePreparedContent?> PrepareAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        Foundry.Core.Services.Configuration.PreOobeConfigurationValidator.ThrowIfInvalid(new PreOobeSettings
        { IsEnabled = context.Request.PreOobe.IsEnabled, Actions = context.Request.PreOobe.Actions });
        if (!IsRequired(context.Request)) return null;
        string rid = ResolveRid(context.Request.OperatingSystem.Architecture);
        PreOobeRuntimeDescriptor descriptor = PreOobeRuntimeResolver.ReadDescriptor(DescriptorPath);
        var asset = descriptor.Assets.SingleOrDefault(item => item.RuntimeIdentifier == rid)
            ?? throw new InvalidDataException("The authenticated Deploy release does not contain the required target runtime.");
        var prepared = new PreOobePreparedContent { RuntimeIdentifier = rid, RuntimeAsset = asset };
        try
        {
            string[] roots = MediaRoots();
            var settings = context.Request.PreOobe;
            var packages = ApplicableActions(context.Request).Where(action => action.Package is not null)
                .Select(action => action.Package!).GroupBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
            var runtimeCandidates = new List<string>();
            if (settings.ManifestId is not null || settings.ManifestHash is not null)
            {
                if (!Guid.TryParseExact(settings.ManifestId, "N", out _) || !PreOobeRuntimeResolver.IsHash(settings.ManifestHash))
                    throw new InvalidDataException("Post-installation media binding is invalid.");
                (string Root, PreOobeMediaManifest Manifest)? media = null;
                foreach (string root in roots)
                {
                    string path = Path.Combine(root, "Cache", "PreOobe", "manifests", settings.ManifestId + ".json");
                    if (!File.Exists(path)) continue;
                    await RequireSeparateSourceAsync(context, path, cancellationToken).ConfigureAwait(false);
                    if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Post-installation media manifest exceeds its size limit.");
                    byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    if (bytes.Length > 16 * 1024 * 1024 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase)) continue;
                    var manifest = JsonSerializer.Deserialize<PreOobeMediaManifest>(bytes, ConfigurationJsonDefaults.SerializerOptions)
                        ?? throw new InvalidDataException("Post-installation media manifest is invalid.");
                    if (manifest.SchemaVersion != 1 || manifest.Id != settings.ManifestId) throw new InvalidDataException("Post-installation media generation is incompatible.");
                    media = (root, manifest);
                    break;
                }
                if (media is null) throw new InvalidDataException("The referenced post-installation media generation is unavailable.");
                foreach (var package in packages)
                {
                    var matches = media.Value.Manifest.Packages.Where(item => item.ContentHash.Equals(package.ContentHash, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("Required post-installation package is missing or ambiguous.");
                    var item = matches[0];
                    if (item.RelativePath != $"Cache/PreOobe/Packages/{package.ContentHash}/files")
                        throw new InvalidDataException("Package content is outside the canonical media cache.");
                    PreOobePackageManifestCodec.Validate(item.Manifest);
                    if (!PreOobePackageManifestCodec.GetContentHash(item.Manifest).Equals(package.ContentHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Package manifest hash differs from its immutable identity.");
                    foreach (var action in ApplicableActions(context.Request).Where(action => action.Package?.ContentHash == package.ContentHash))
                    {
                        if (action.EntryPoint is not null && !item.Manifest.Files.Any(file => file.RelativePath.Equals(action.EntryPoint, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException("The configured entry point is missing from the package.");
                        if (action.WorkingDirectory is not null && !item.Manifest.Directories.Contains(action.WorkingDirectory, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException("The configured working directory is missing from the package.");
                    }
                    string source = Path.Combine(media.Value.Root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (item.Manifest.SchemaVersion != 1 || item.Manifest.Files.Count != package.FileCount || item.Manifest.Files.Sum(file => file.Length) != package.Length)
                        throw new InvalidDataException("Post-installation package identity does not match its reference.");
                    foreach (string relative in item.Manifest.Files.Select(file => file.RelativePath).Concat(item.Manifest.Directories))
                        PreOobePackagePathPolicy.Resolve($@"C:\Windows\Temp\Foundry\Payloads\PostInstall\{new string('0', 32)}\{package.ContentHash}", relative);
                    foreach (var file in item.Manifest.Files)
                    {
                        PreOobeRuntimeResolver.ValidateRelativePath(file.RelativePath);
                        string filePath = PreOobePackagePathPolicy.Resolve(source, file.RelativePath);
                        await RequireSeparateSourceAsync(context, filePath, cancellationToken).ConfigureAwait(false);
                        prepared.Files.Add((filePath, await OpenVerifiedAsync(filePath, file.Length, file.Sha256, cancellationToken).ConfigureAwait(false)));
                    }
                    prepared.Packages.Add(new(package.ContentHash, source, item.Manifest));
                }
                foreach (var runtime in media.Value.Manifest.Runtimes.Where(item => item.RuntimeIdentifier == rid && item.ArchiveSha256.Equals(asset.ArchiveSha256, StringComparison.OrdinalIgnoreCase)))
                {
                    if (runtime.RelativePath != $"Cache/PreOobe/Runtimes/{rid}/{asset.ArchiveSha256}/runtime.zip")
                        throw new InvalidDataException("Runtime content is outside the canonical media cache.");
                    runtimeCandidates.Add(Path.Combine(media.Value.Root, runtime.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
            else if (packages.Length != 0) throw new InvalidDataException("Package actions require an authenticated external media manifest.");
            foreach (string root in roots)
            {
                string path = Path.Combine(root, "Cache", "PreOobe", "Runtimes", rid, asset.ArchiveSha256, "runtime.zip");
                if (File.Exists(path)) { await RequireSeparateSourceAsync(context, path, cancellationToken).ConfigureAwait(false); runtimeCandidates.Add(path); }
            }
            byte[] planInputs = JsonSerializer.SerializeToUtf8Bytes(new
            {
                actions = ApplicableActions(context.Request).ToArray(),
                packages = prepared.Packages.Select(package => new { package.ContentHash, package.Manifest }).ToArray(),
                context.Request.AppxRemoval,
                context.Request.AiComponentRemoval
            }, ConfigurationJsonDefaults.SerializerOptions);
            if (planInputs.Length > 7 * 1024 * 1024)
                throw new InvalidDataException("Post-installation inputs exceed the bounded execution-plan budget.");
            foreach (string candidate in runtimeCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await PreOobeRuntimeResolver.VerifyAsync(candidate, asset, cancellationToken).ConfigureAwait(false);
                    prepared.RuntimeArchivePath = candidate;
                    prepared.Files.Add((candidate, await OpenVerifiedAsync(candidate, asset.ArchiveLength, asset.ArchiveSha256, cancellationToken).ConfigureAwait(false)));
                    return prepared;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { }
            }
            string writable = Path.Combine(context.Request.CacheRootPath, "PreOobe", "Runtimes", rid, asset.ArchiveSha256);
            long required = checked(asset.ArchiveLength + asset.ExpandedLength + DeploymentCapacityPolicy.ScratchAndHeadroomBytes);
            if (!await context.IsExternalStorageAsync(writable, cancellationToken).ConfigureAwait(false) ||
                storage.GetAvailableBytes(writable) is not long free || free < required || !storage.CanWriteDirectory(writable))
            {
                writable = Path.Combine(@"X:\Foundry\Temp\PostInstall", Guid.NewGuid().ToString("N"));
                if (storage.GetAvailableBytes(writable) is not long ramAvailable || ramAvailable < required)
                    throw new InvalidDataException("There is insufficient reserved WinPE storage for the matching post-installation runtime.");
                prepared.TemporaryRuntimeDirectory = writable;
            }
            prepared.RuntimeArchivePath = await runtimeResolver.ResolveAsync(descriptor, asset, runtimeCandidates, writable, cancellationToken).ConfigureAwait(false);
            prepared.Files.Add((prepared.RuntimeArchivePath, await OpenVerifiedAsync(prepared.RuntimeArchivePath, asset.ArchiveLength, asset.ArchiveSha256, cancellationToken).ConfigureAwait(false)));
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    internal static async Task RevalidateAsync(DeploymentStepExecutionContext context, PreOobePreparedContent content, CancellationToken cancellationToken)
    {
        foreach (var file in content.Files)
        {
            if (!file.Stream.CanRead || !File.Exists(file.Path)) throw new InvalidDataException("Post-installation source is no longer available.");
            if (content.TemporaryRuntimeDirectory is null || !file.Path.StartsWith(content.TemporaryRuntimeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                await RequireSeparateSourceAsync(context, file.Path, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RequireSeparateSourceAsync(DeploymentStepExecutionContext context, string path, CancellationToken cancellationToken)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.DriveType == DriveType.CDRom) return;
        if (!await context.IsExternalStorageAsync(path, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Post-installation source must remain on verified storage separate from the target disk.");
    }

    internal static async Task<FileStream> OpenVerifiedAsync(string path, long length, string hash, CancellationToken cancellationToken)
    {
        if (length < 0 || !PreOobeRuntimeResolver.IsHash(hash)) throw new InvalidDataException("Invalid package file metadata.");
        CustomImageSourceLease.EnsureRegularPath(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        try
        {
            if (stream.Length != length || !Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Post-installation content failed integrity verification.");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
