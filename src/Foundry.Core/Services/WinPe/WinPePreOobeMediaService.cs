// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Post-installation facade over <see cref="WinPeContentPackageMediaService"/>: it validates the action settings
/// (entry points and working directories) and binds the media generation to the deploy configuration.
/// </summary>
public sealed class WinPePreOobeMediaService
{
    private readonly WinPeContentPackageMediaService mediaService = new(ContentPackageKind.PostInstallation);

    /// <summary>Freezes enabled package versions before boot configuration is generated.</summary>
    public async Task<WinPeContentPackageMediaLease> PrepareAsync(ContentPackageLibraryService library, PreOobeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        PreOobeConfigurationValidator.ThrowIfInvalid(settings);
        PreOobeActionSettings[] actions = settings.Actions.Where(action => settings.IsEnabled && action.IsEnabled && action.Package is not null).ToArray();
        WinPeContentPackageMediaLease lease = await mediaService.PrepareAsync(library, actions.Select(action => action.Package!).ToArray(), cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (PreOobeActionSettings action in actions)
            {
                ContentPackageMediaPackage package = lease.Manifest.Packages.Single(entry => entry.ContentHash.Equals(action.Package!.ContentHash, StringComparison.OrdinalIgnoreCase));
                if (package.Manifest.Files.Count != action.Package!.FileCount || package.Manifest.Files.Sum(file => file.Length) != action.Package.Length ||
                    (action.EntryPoint is not null && !package.Manifest.Files.Any(file => file.RelativePath.Equals(action.EntryPoint, StringComparison.OrdinalIgnoreCase))) ||
                    (action.WorkingDirectory is not null && !package.Manifest.Directories.Contains(action.WorkingDirectory, StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidDataException("PreOobe.MissingEntryPoint");
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static string BindConfiguration(WinPeContentPackageMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("PreOobe.InvalidConfiguration");
        string bound = JsonSerializer.Serialize(configuration with
        {
            PreOobe = configuration.PreOobe with { ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        ValidateConfigurationBinding(package, bound);
        return bound;
    }

    public static void ValidateConfigurationBinding(WinPeContentPackageMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions);
        DeployPreOobeSettings? settings = configuration?.PreOobe;
        if (settings is null || settings.ManifestId != package.ManifestId || !package.ManifestHash.Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PreOobe.InvalidMediaBinding");
        PreOobeConfigurationValidator.ThrowIfInvalid(new() { IsEnabled = settings.IsEnabled, Actions = settings.Actions });
        string[] required = settings.Actions.Where(action => settings.IsEnabled && action.IsEnabled && action.Package is not null)
            .Select(action => action.Package!.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] supplied = package.Manifest.Packages.Select(entry => entry.ContentHash).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!required.SequenceEqual(supplied, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("PreOobe.InvalidMediaBinding");
    }
}
