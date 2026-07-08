// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Services.WinPe;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Represents a single PowerShell Gallery search result shown in the boot image module picker, including the
/// selectable list of published versions.
/// </summary>
public sealed partial class BootImageModuleSearchResultViewModel : ObservableObject
{
    private readonly IPowerShellGalleryModuleSearchService searchService;
    private readonly ILogger logger;
    private bool versionsLoaded;

    public BootImageModuleSearchResultViewModel(
        PowerShellGalleryModule module,
        string addLabel,
        IPowerShellGalleryModuleSearchService searchService,
        ILogger logger)
    {
        Module = module;
        AddLabel = addLabel;
        this.searchService = searchService;
        this.logger = logger;

        // Seed with the latest version so the selector shows a sensible default before versions are loaded.
        Versions.Add(module.Version);
        SelectedVersion = module.Version;
    }

    /// <summary>
    /// Gets the underlying Gallery module.
    /// </summary>
    public PowerShellGalleryModule Module { get; }

    /// <summary>
    /// Gets the localized label for the add action.
    /// </summary>
    public string AddLabel { get; }

    /// <summary>
    /// Gets the module display name.
    /// </summary>
    public string Name => Module.Name;

    /// <summary>
    /// Gets the module description.
    /// </summary>
    public string Description => Module.Description;

    /// <summary>
    /// Gets the published versions, newest first (index 0 is the latest).
    /// </summary>
    public ObservableCollection<string> Versions { get; } = [];

    [ObservableProperty]
    public partial string? SelectedVersion { get; set; }

    /// <summary>
    /// Loads the full version list from the Gallery the first time the version selector is opened.
    /// </summary>
    public async Task EnsureVersionsLoadedAsync()
    {
        if (versionsLoaded)
        {
            return;
        }

        versionsLoaded = true;
        WinPeResult<IReadOnlyList<string>> result = await searchService.GetVersionsAsync(Module.Name);
        if (!result.IsSuccess || result.Value is not { Count: > 0 } versions)
        {
            logger.Warning(
                "PowerShell Gallery version lookup returned no versions for {ModuleName}. ErrorCode={ErrorCode}",
                Module.Name,
                result.Error?.Code);
            return;
        }

        string? previousSelection = SelectedVersion;
        Versions.Clear();
        foreach (string version in versions)
        {
            Versions.Add(version);
        }

        // Keep the previous selection when still present; otherwise default to the latest version.
        SelectedVersion = versions.Contains(previousSelection, StringComparer.OrdinalIgnoreCase)
            ? previousSelection
            : versions[0];
    }
}
