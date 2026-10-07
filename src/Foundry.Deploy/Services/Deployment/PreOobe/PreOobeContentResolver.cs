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
public class PreOobeContentResolver
{
    internal string? RuntimeExecutablePath { get; init; } = Environment.GetEnvironmentVariable(PostInstallRuntimeManifest.ExecutableEnvironmentVariable);
    internal PostInstallRuntimeRecovery RuntimeRecovery { get; init; } = new();
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
        PreOobePreparedContent prepared;
        if (string.IsNullOrWhiteSpace(RuntimeExecutablePath))
        {
            string message = Services.Localization.LocalizationText.GetString("PostInstall.RuntimePreparing");
            context.EmitCurrentStepIndeterminate(message, message, DeploymentOperationNames.PreflightDeployment);
            string? cacheRoot = context.Request.Mode == Models.DeploymentMode.Usb ? context.RuntimeState.ResolvedCache?.RootPath : null;
            if (cacheRoot is not null && !await context.IsExternalStorageAsync(cacheRoot, cancellationToken).ConfigureAwait(false)) cacheRoot = null;
            prepared = await RuntimeRecovery.AcquireAsync(rid, cacheRoot, cancellationToken,
                context.CreateDownloadProgressReporter("Foundry.PostInstall", DeploymentOperationNames.PreflightDeployment)).ConfigureAwait(false);
        }
        else
        {
            prepared = await PostInstallRuntimeSource.AcquireAsync(RuntimeExecutablePath, rid, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            await RequireRuntimeSourceAsync(context, prepared.RuntimeDirectory, cancellationToken).ConfigureAwait(false);
            var settings = context.Request.PreOobe;
            var packages = ApplicableActions(context.Request).Where(action => action.Package is not null)
                .Select(action => action.Package!).GroupBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
            // Only package actions consume the external media generation. Built-in tasks and package-less actions
            // run from the boot configuration alone, so a boot image started without its media must not be blocked.
            if (packages.Length != 0)
            {
                if (settings.ManifestId is null && settings.ManifestHash is null)
                    throw new InvalidDataException("Package actions require an authenticated external media manifest.");
                if (!Guid.TryParseExact(settings.ManifestId, "N", out _) || !PreOobePackagePathPolicy.IsValidHash(settings.ManifestHash))
                    throw new InvalidDataException("Post-installation media binding is invalid.");
                (string Root, PreOobeMediaManifest Manifest)? media = null;
                foreach (string root in MediaRoots())
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
                        PreOobePackagePathPolicy.ResolveLexically($@"C:\Windows\Temp\Foundry\Payloads\PostInstall\{new string('0', 32)}\{package.ContentHash}", relative);
                    foreach (var file in item.Manifest.Files)
                    {
                        PreOobePackagePathPolicy.ValidateRelativePath(file.RelativePath);
                        string filePath = PreOobePackagePathPolicy.Resolve(source, file.RelativePath);
                        await RequireSeparateSourceAsync(context, filePath, cancellationToken).ConfigureAwait(false);
                        prepared.Files.Add((filePath, await OpenVerifiedAsync(filePath, file.Length, file.Sha256, cancellationToken).ConfigureAwait(false)));
                    }
                    prepared.Packages.Add(new(package.ContentHash, source, item.Manifest));
                }
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
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    internal static async Task RevalidateAsync(DeploymentStepExecutionContext context, PreOobePreparedContent content, CancellationToken cancellationToken)
    {
        await RequireRuntimeSourceAsync(context, content.RuntimeDirectory, cancellationToken).ConfigureAwait(false);
        foreach (var file in content.Files)
        {
            if (!file.Stream.CanRead || !File.Exists(file.Path)) throw new InvalidDataException("Post-installation source is no longer available.");
            if (!file.Path.StartsWith(content.RuntimeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                await RequireSeparateSourceAsync(context, file.Path, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task RequireRuntimeSourceAsync(DeploymentStepExecutionContext context, string path, CancellationToken cancellationToken) =>
        string.Equals(Path.GetPathRoot(path), @"X:\", StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask
            : RequireSeparateSourceAsync(context, path, cancellationToken);

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
        if (length < 0 || !PreOobePackagePathPolicy.IsValidHash(hash)) throw new InvalidDataException("Invalid package file metadata.");
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
