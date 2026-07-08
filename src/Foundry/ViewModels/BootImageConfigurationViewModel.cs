// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the expert boot image page and persists optional components, PowerShell integration,
/// extra modules, root folder overlays, and boot behavior toggles.
/// </summary>
public sealed partial class BootImageConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IAdkService adkService;
    private readonly IWinPeOptionalComponentCatalogService optionalComponentCatalogService;
    private readonly IPowerShell7ReleaseService powerShell7ReleaseService;
    private readonly IPowerShellGalleryModuleSearchService moduleSearchService;
    private readonly IFilePickerService filePickerService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isInitializing = true;

    public BootImageConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        IAdkService adkService,
        IWinPeOptionalComponentCatalogService optionalComponentCatalogService,
        IPowerShell7ReleaseService powerShell7ReleaseService,
        IPowerShellGalleryModuleSearchService moduleSearchService,
        IFilePickerService filePickerService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.adkService = adkService;
        this.optionalComponentCatalogService = optionalComponentCatalogService;
        this.powerShell7ReleaseService = powerShell7ReleaseService;
        this.moduleSearchService = moduleSearchService;
        this.filePickerService = filePickerService;
        this.localizationService = localizationService;
        this.logger = logger.ForContext<BootImageConfigurationViewModel>();

        WinPeBootImageContentSettings settings = configurationStateService.Current.General.BootImageContent;
        EnableFirewall = settings.EnableFirewall;
        IncludeTroubleshootingConsole = settings.IncludeTroubleshootingConsole;
        KeepBootWimCopy = settings.KeepBootWimCopy;
        IncludePowerShell7 = settings.IncludePowerShell7;

        foreach (PowerShellModuleSelection module in settings.PowerShellModules)
        {
            SelectedModules.Add(new BootImageModuleViewModel(module));
        }

        foreach (string folder in settings.AdditionalRootFolderPaths)
        {
            AdditionalRootFolders.Add(folder);
        }

        RefreshLocalizedText();
        LoadOptionalComponents();

        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isInitializing = false;
    }

    /// <summary>
    /// Gets the WinPE optional components discovered from the ADK, with recommended defaults pre-checked.
    /// </summary>
    public ObservableCollection<SelectableStringOptionViewModel> OptionalComponents { get; } = [];

    /// <summary>
    /// Gets the latest stable PowerShell 7 releases available for selection.
    /// </summary>
    public ObservableCollection<SelectionOption<string>> PowerShell7Versions { get; } = [];

    /// <summary>
    /// Gets the PowerShell Gallery search results for the current search term.
    /// </summary>
    public ObservableCollection<PowerShellGalleryModule> ModuleSearchResults { get; } = [];

    /// <summary>
    /// Gets the PowerShell modules selected for integration.
    /// </summary>
    public ObservableCollection<BootImageModuleViewModel> SelectedModules { get; } = [];

    /// <summary>
    /// Gets the additional folders whose contents are copied into the boot image root.
    /// </summary>
    public ObservableCollection<string> AdditionalRootFolders { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PageDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EnableFirewall { get; set; }

    [ObservableProperty]
    public partial bool IncludeTroubleshootingConsole { get; set; }

    [ObservableProperty]
    public partial bool KeepBootWimCopy { get; set; }

    [ObservableProperty]
    public partial bool IncludePowerShell7 { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedPowerShell7Version { get; set; }

    [ObservableProperty]
    public partial string OptionalComponentStatus { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(OptionalComponentStatusVisibility))]
    [ObservableProperty]
    public partial bool HasOptionalComponents { get; set; }

    /// <summary>
    /// Gets whether the optional component status message is shown (when no components are available).
    /// </summary>
    public Visibility OptionalComponentStatusVisibility =>
        HasOptionalComponents ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial string PowerShell7Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleSearchTerm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleSearchStatus { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(ModuleSearchProgressVisibility))]
    [ObservableProperty]
    public partial bool IsSearchingModules { get; set; }

    /// <summary>
    /// Gets whether the module search progress indicator is shown.
    /// </summary>
    public Visibility ModuleSearchProgressVisibility =>
        IsSearchingModules ? Visibility.Visible : Visibility.Collapsed;

    public string DocumentationUrl => FoundryApplicationInfo.GeneralConfigurationDocumentationUrl;

    /// <inheritdoc />
    public void Dispose()
    {
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        foreach (SelectableStringOptionViewModel component in OptionalComponents)
        {
            component.PropertyChanged -= OnOptionalComponentChanged;
        }
    }

    public void RefreshLocalizedText()
    {
        PageTitle = localizationService.GetString("BootImagePage_Title.Text");
        PageDescription = localizationService.GetString("BootImage.PageDescription");
    }

    /// <summary>
    /// Reloads the optional component catalog from the ADK and PowerShell 7 releases when the page appears.
    /// </summary>
    public async Task RefreshAsync()
    {
        LoadOptionalComponents();
        if (IncludePowerShell7)
        {
            await RefreshPowerShell7VersionsAsync();
        }
    }

    private void LoadOptionalComponents()
    {
        foreach (SelectableStringOptionViewModel existing in OptionalComponents)
        {
            existing.PropertyChanged -= OnOptionalComponentChanged;
        }

        OptionalComponents.Clear();

        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            HasOptionalComponents = false;
            OptionalComponentStatus = localizationService.GetString("BootImage.OptionalComponents.AdkBlocked");
            return;
        }

        WinPeArchitecture architecture = configurationStateService.Current.General.Architecture;
        WinPeResult<IReadOnlyList<WinPeOptionalComponent>> result =
            optionalComponentCatalogService.GetAvailableComponents(adkService.CurrentStatus.KitsRootPath, architecture);

        if (!result.IsSuccess || result.Value is not { Count: > 0 } components)
        {
            HasOptionalComponents = false;
            OptionalComponentStatus = localizationService.GetString("BootImage.OptionalComponents.Unavailable");
            logger.Warning(
                "WinPE optional component discovery returned no components. Architecture={Architecture}, ErrorCode={ErrorCode}",
                architecture,
                result.Error?.Code);
            return;
        }

        IReadOnlyList<string> persisted = configurationStateService.Current.General.BootImageContent.OptionalComponents;
        bool useRecommendedDefaults = persisted.Count == 0;

        int sortOrder = 0;
        foreach (WinPeOptionalComponent component in components)
        {
            bool isSelected = useRecommendedDefaults
                ? component.IsRecommendedDefault
                : persisted.Contains(component.Name, StringComparer.OrdinalIgnoreCase);

            SelectableStringOptionViewModel option = new(component.Name, component.Name, sortOrder++, isSelected);
            option.PropertyChanged += OnOptionalComponentChanged;
            OptionalComponents.Add(option);
        }

        HasOptionalComponents = true;
        OptionalComponentStatus = string.Empty;

        // Persist the resolved selection so an explicit list is stored (recommended defaults on first use).
        SaveOptionalComponents();
    }

    private async Task RefreshPowerShell7VersionsAsync()
    {
        PowerShell7Status = localizationService.GetString("BootImage.PowerShell7.Loading");
        PowerShell7Versions.Clear();

        WinPeArchitecture architecture = configurationStateService.Current.General.Architecture;
        WinPeResult<IReadOnlyList<PowerShell7Release>> result =
            await powerShell7ReleaseService.GetLatestStableReleasesAsync(architecture, 3);

        if (!result.IsSuccess || result.Value is not { Count: > 0 } releases)
        {
            PowerShell7Status = localizationService.GetString("BootImage.PowerShell7.Unavailable");
            logger.Warning("PowerShell 7 release lookup returned no releases. ErrorCode={ErrorCode}", result.Error?.Code);
            return;
        }

        foreach (PowerShell7Release release in releases)
        {
            PowerShell7Versions.Add(new SelectionOption<string>(release.Version, release.Version));
        }

        string? persistedVersion = configurationStateService.Current.General.BootImageContent.PowerShell7Version;
        SelectedPowerShell7Version = PowerShell7Versions.FirstOrDefault(option =>
            string.Equals(option.Value, persistedVersion, StringComparison.OrdinalIgnoreCase)) ?? PowerShell7Versions[0];
        PowerShell7Status = string.Empty;
    }

    [RelayCommand]
    private async Task SearchModulesAsync()
    {
        string term = ModuleSearchTerm.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        IsSearchingModules = true;
        ModuleSearchResults.Clear();
        ModuleSearchStatus = localizationService.GetString("BootImage.Modules.Searching");

        try
        {
            WinPeResult<IReadOnlyList<PowerShellGalleryModule>> result = await moduleSearchService.SearchAsync(term, 20);
            if (!result.IsSuccess || result.Value is not { Count: > 0 } modules)
            {
                ModuleSearchStatus = localizationService.GetString("BootImage.Modules.NoResults");
                return;
            }

            foreach (PowerShellGalleryModule module in modules)
            {
                ModuleSearchResults.Add(module);
            }

            ModuleSearchStatus = string.Empty;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "PowerShell Gallery module search failed. SearchTerm={SearchTerm}", term);
            ModuleSearchStatus = localizationService.GetString("BootImage.Modules.SearchFailed");
        }
        finally
        {
            IsSearchingModules = false;
        }
    }

    /// <summary>
    /// Adds a Gallery module search result to the selected modules list.
    /// </summary>
    public void AddGalleryModule(PowerShellGalleryModule module)
    {
        if (SelectedModules.Any(selected =>
                selected.Selection.Source == PowerShellModuleSource.Gallery &&
                string.Equals(selected.Selection.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedModules.Add(new BootImageModuleViewModel(new PowerShellModuleSelection
        {
            Source = PowerShellModuleSource.Gallery,
            Name = module.Name,
            Version = module.Version
        }));
        SaveModules();
    }

    [RelayCommand]
    private async Task AddLocalModuleAsync()
    {
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("BootImage.Modules.LocalPicker.Title")));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string name = new DirectoryInfo(path).Name;
        SelectedModules.Add(new BootImageModuleViewModel(new PowerShellModuleSelection
        {
            Source = PowerShellModuleSource.Local,
            Name = name,
            LocalPath = path
        }));
        SaveModules();
    }

    /// <summary>
    /// Removes a module from the selected modules list.
    /// </summary>
    public void RemoveModule(BootImageModuleViewModel module)
    {
        if (SelectedModules.Remove(module))
        {
            SaveModules();
        }
    }

    [RelayCommand]
    private async Task AddRootFolderAsync()
    {
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("BootImage.RootFolders.Picker.Title")));

        if (string.IsNullOrWhiteSpace(path) ||
            AdditionalRootFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AdditionalRootFolders.Add(path);
        SaveRootFolders();
    }

    /// <summary>
    /// Removes a folder from the additional root folders list.
    /// </summary>
    public void RemoveRootFolder(string folder)
    {
        if (AdditionalRootFolders.Remove(folder))
        {
            SaveRootFolders();
        }
    }

    partial void OnEnableFirewallChanged(bool value) => Save(current => current with { EnableFirewall = value });

    partial void OnIncludeTroubleshootingConsoleChanged(bool value) =>
        Save(current => current with { IncludeTroubleshootingConsole = value });

    partial void OnKeepBootWimCopyChanged(bool value) => Save(current => current with { KeepBootWimCopy = value });

    partial void OnIncludePowerShell7Changed(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        Save(current => current with { IncludePowerShell7 = value });
        if (value && PowerShell7Versions.Count == 0)
        {
            _ = RefreshPowerShell7VersionsAsync();
        }
    }

    partial void OnSelectedPowerShell7VersionChanged(SelectionOption<string>? value)
    {
        if (isInitializing || value is null)
        {
            return;
        }

        Save(current => current with { PowerShell7Version = value.Value });
    }

    private void OnOptionalComponentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (isInitializing || e.PropertyName != nameof(SelectableStringOptionViewModel.IsSelected))
        {
            return;
        }

        SaveOptionalComponents();
    }

    private void SaveOptionalComponents()
    {
        List<string> selected = OptionalComponents
            .Where(component => component.IsSelected)
            .Select(component => component.Value)
            .ToList();

        Save(current => current with { OptionalComponents = selected });
    }

    private void SaveModules()
    {
        List<PowerShellModuleSelection> modules = SelectedModules.Select(module => module.Selection).ToList();
        Save(current => current with { PowerShellModules = modules });
    }

    private void SaveRootFolders()
    {
        Save(current => current with { AdditionalRootFolderPaths = AdditionalRootFolders.ToList() });
    }

    private void Save(Func<WinPeBootImageContentSettings, WinPeBootImageContentSettings> transform)
    {
        if (isInitializing)
        {
            return;
        }

        GeneralSettings general = configurationStateService.Current.General;
        configurationStateService.UpdateGeneral(general with
        {
            BootImageContent = transform(general.BootImageContent)
        });
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        // State changes are driven by this view model; no external refresh is required.
    }
}
