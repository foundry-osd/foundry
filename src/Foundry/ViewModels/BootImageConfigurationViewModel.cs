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
using Windows.ApplicationModel.DataTransfer;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the expert Boot Image page and persists optional components, PowerShell integration, extra modules,
/// root folder overlays, and boot behavior toggles. The page is organized into in-page sub-sections navigated
/// by a left section rail.
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
    private IReadOnlyList<PowerShell7Release> _powerShell7Releases = [];

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

        GeneralSettings general = configurationStateService.Current.General;
        WinPeBootImageContentSettings settings = general.BootImageContent;
        EnableFirewall = settings.EnableFirewall;
        IncludeTroubleshootingConsole = settings.IncludeTroubleshootingConsole;
        KeepBootWimCopy = settings.KeepBootWimCopy;
        IncludePowerShell7 = settings.IncludePowerShell7;
        IncludeDellDrivers = general.IncludeDellDrivers;
        IncludeHpDrivers = general.IncludeHpDrivers;
        ContinueOnDriverError = settings.ContinueOnDriverError;

        foreach (PowerShellModuleSelection module in settings.PowerShellModules)
        {
            ModuleListFor(module.Source).Add(new BootImageModuleViewModel(module, RemoveLabel));
        }

        foreach (WinPeAdditionalRootFolder folder in settings.AdditionalRootFolders)
        {
            AdditionalRootFolders.Add(new BootImageAdditionalFolderViewModel(
                folder.SourcePath, folder.DestinationRelativePath, RemoveLabel, SourceLabel, DestinationLabel, SaveRootFolders));
        }

        // Migrate a legacy single custom driver directory into the driver folder list on first use.
        bool migrateLegacyDriverFolder = settings.DriverFolders.Count == 0
            && !string.IsNullOrWhiteSpace(general.CustomDriverDirectoryPath);
        IReadOnlyList<WinPeDriverFolder> driverFolders = migrateLegacyDriverFolder
            ? [new WinPeDriverFolder { Path = general.CustomDriverDirectoryPath! }]
            : settings.DriverFolders;
        foreach (WinPeDriverFolder folder in driverFolders)
        {
            DriverFolders.Add(new BootImageDriverFolderViewModel(folder.Path, folder.IsEnabled, RemoveLabel, SaveDriverFolders));
        }

        if (migrateLegacyDriverFolder)
        {
            configurationStateService.UpdateGeneral(general with
            {
                BootImageContent = settings with { DriverFolders = driverFolders.ToList() }
            });
        }

        DriverFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDriverFolders));
        AdditionalRootFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAdditionalRootFolders));

        RefreshLocalizedText();
        SelectedSectionItem = Sections.FirstOrDefault();
        LoadOptionalComponents();

        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isInitializing = false;
    }

    /// <summary>
    /// Gets the sub-section entries shown in the left section rail.
    /// </summary>
    public ObservableCollection<SelectionOption<BootImageSection>> Sections { get; } = [];

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
    public ObservableCollection<BootImageModuleSearchResultViewModel> ModuleSearchResults { get; } = [];

    /// <summary>
    /// Gets the selected PowerShell Gallery modules.
    /// </summary>
    public ObservableCollection<BootImageModuleViewModel> SelectedGalleryModules { get; } = [];

    /// <summary>
    /// Gets the selected local-folder modules.
    /// </summary>
    public ObservableCollection<BootImageModuleViewModel> SelectedLocalModules { get; } = [];

    /// <summary>
    /// Gets the additional folders whose contents are copied into a relative destination inside the boot image.
    /// </summary>
    public ObservableCollection<BootImageAdditionalFolderViewModel> AdditionalRootFolders { get; } = [];

    /// <summary>
    /// Gets the folders that contain drivers (.inf packages) to inject into the boot image.
    /// </summary>
    public ObservableCollection<BootImageDriverFolderViewModel> DriverFolders { get; } = [];

    private string AddLabel => localizationService.GetString("Common.Add");

    private string RemoveLabel => localizationService.GetString("Common.Remove");

    private string SourceLabel => localizationService.GetString("BootImage.Folders.SourceLabel");

    private string DestinationLabel => localizationService.GetString("BootImage.Folders.DestinationLabel");

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PageDescription { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(SettingsVisibility))]
    [NotifyPropertyChangedFor(nameof(DriversVisibility))]
    [NotifyPropertyChangedFor(nameof(OptionalComponentsVisibility))]
    [NotifyPropertyChangedFor(nameof(PowerShellVisibility))]
    [NotifyPropertyChangedFor(nameof(ModulesVisibility))]
    [NotifyPropertyChangedFor(nameof(AdditionalFoldersVisibility))]
    [ObservableProperty]
    public partial SelectionOption<BootImageSection>? SelectedSectionItem { get; set; }

    public Visibility SettingsVisibility => VisibilityFor(BootImageSection.Settings);

    public Visibility DriversVisibility => VisibilityFor(BootImageSection.Drivers);

    public Visibility OptionalComponentsVisibility => VisibilityFor(BootImageSection.OptionalComponents);

    public Visibility PowerShellVisibility => VisibilityFor(BootImageSection.PowerShell);

    public Visibility ModulesVisibility => VisibilityFor(BootImageSection.Modules);

    public Visibility AdditionalFoldersVisibility => VisibilityFor(BootImageSection.AdditionalFolders);

    [ObservableProperty]
    public partial bool EnableFirewall { get; set; }

    [ObservableProperty]
    public partial bool IncludeTroubleshootingConsole { get; set; }

    [ObservableProperty]
    public partial bool KeepBootWimCopy { get; set; }

    [ObservableProperty]
    public partial bool IncludeDellDrivers { get; set; }

    [ObservableProperty]
    public partial bool IncludeHpDrivers { get; set; }

    [ObservableProperty]
    public partial bool ContinueOnDriverError { get; set; }

    [ObservableProperty]
    public partial bool IncludePowerShell7 { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedPowerShell7Version { get; set; }

    [NotifyPropertyChangedFor(nameof(PowerShell7DownloadVisibility))]
    [ObservableProperty]
    public partial string SelectedPowerShell7DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether the download link for the selected PowerShell 7 release is shown.
    /// </summary>
    public Visibility PowerShell7DownloadVisibility =>
        string.IsNullOrWhiteSpace(SelectedPowerShell7DownloadUrl) ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial string OptionalComponentStatus { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(OptionalComponentStatusVisibility))]
    [ObservableProperty]
    public partial bool HasOptionalComponents { get; set; }

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

    public Visibility ModuleSearchProgressVisibility =>
        IsSearchingModules ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Gets whether the driver folder list (and its backdrop) is shown.
    /// </summary>
    public Visibility HasDriverFolders => DriverFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Gets whether the additional folder list (and its backdrop) is shown.
    /// </summary>
    public Visibility HasAdditionalRootFolders => AdditionalRootFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        RebuildSections();
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

    private void RebuildSections()
    {
        BootImageSection? previous = SelectedSectionItem?.Value;

        Sections.Clear();
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.Settings, localizationService.GetString("BootImage.Section.Settings")));
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.Drivers, localizationService.GetString("BootImage.Section.Drivers")));
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.OptionalComponents, localizationService.GetString("BootImage.Section.OptionalComponents")));
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.PowerShell, localizationService.GetString("BootImage.Section.PowerShell")));
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.Modules, localizationService.GetString("BootImage.Section.Modules")));
        Sections.Add(new SelectionOption<BootImageSection>(BootImageSection.AdditionalFolders, localizationService.GetString("BootImage.Section.AdditionalFolders")));

        SelectedSectionItem = Sections.FirstOrDefault(section => section.Value == previous) ?? Sections[0];
    }

    private Visibility VisibilityFor(BootImageSection section) =>
        SelectedSectionItem?.Value == section ? Visibility.Visible : Visibility.Collapsed;

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
        _powerShell7Releases = [];

        WinPeArchitecture architecture = configurationStateService.Current.General.Architecture;
        WinPeResult<IReadOnlyList<PowerShell7Release>> result =
            await powerShell7ReleaseService.GetLatestStableReleasesAsync(architecture, 10);

        if (!result.IsSuccess || result.Value is not { Count: > 0 } releases)
        {
            PowerShell7Status = localizationService.GetString("BootImage.PowerShell7.Unavailable");
            logger.Warning("PowerShell 7 release lookup returned no releases. ErrorCode={ErrorCode}", result.Error?.Code);
            return;
        }

        _powerShell7Releases = releases;
        foreach (PowerShell7Release release in releases)
        {
            PowerShell7Versions.Add(new SelectionOption<string>(release.Version, release.Version));
        }

        string? persistedVersion = configurationStateService.Current.General.BootImageContent.PowerShell7Version;
        SelectedPowerShell7Version = PowerShell7Versions.FirstOrDefault(option =>
            string.Equals(option.Value, persistedVersion, StringComparison.OrdinalIgnoreCase)) ?? PowerShell7Versions[0];
        UpdateSelectedDownloadUrl();
        PowerShell7Status = string.Empty;
    }

    private void UpdateSelectedDownloadUrl()
    {
        SelectedPowerShell7DownloadUrl = _powerShell7Releases
            .FirstOrDefault(release => string.Equals(release.Version, SelectedPowerShell7Version?.Value, StringComparison.OrdinalIgnoreCase))?
            .DownloadUrl ?? string.Empty;
    }

    [RelayCommand]
    private void CopyDownloadUrl()
    {
        if (string.IsNullOrWhiteSpace(SelectedPowerShell7DownloadUrl))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(SelectedPowerShell7DownloadUrl);
        Clipboard.SetContent(package);
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
                ModuleSearchResults.Add(new BootImageModuleSearchResultViewModel(module, AddLabel, moduleSearchService, logger));
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
    /// Adds a Gallery search result (with its selected version) to the selected modules list.
    /// </summary>
    public void AddGalleryModule(BootImageModuleSearchResultViewModel result)
    {
        string version = result.SelectedVersion ?? result.Module.Version;
        if (SelectedGalleryModules.Any(selected =>
                string.Equals(selected.Selection.Name, result.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Selection.Version, version, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedGalleryModules.Add(new BootImageModuleViewModel(
            new PowerShellModuleSelection
            {
                Source = PowerShellModuleSource.Gallery,
                Name = result.Name,
                Version = version
            },
            RemoveLabel));
        SaveModules();
    }

    [RelayCommand]
    private async Task AddLocalModuleAsync()
    {
        // The user selects a module version folder (Save-Module layout: ModuleName\ModuleVersion).
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("BootImage.Modules.LocalPicker.Title")));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DirectoryInfo versionFolder = new(path);
        string version = versionFolder.Name;
        string name = versionFolder.Parent?.Name ?? version;

        if (SelectedLocalModules.Any(selected =>
                string.Equals(selected.Selection.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedLocalModules.Add(new BootImageModuleViewModel(
            new PowerShellModuleSelection
            {
                Source = PowerShellModuleSource.Local,
                Name = name,
                Version = version,
                LocalPath = path
            },
            RemoveLabel));
        SaveModules();
    }

    /// <summary>
    /// Removes a module from whichever selected modules list contains it.
    /// </summary>
    public void RemoveModule(BootImageModuleViewModel module)
    {
        if (ModuleListFor(module.Selection.Source).Remove(module))
        {
            SaveModules();
        }
    }

    private ObservableCollection<BootImageModuleViewModel> ModuleListFor(PowerShellModuleSource source) =>
        source == PowerShellModuleSource.Local ? SelectedLocalModules : SelectedGalleryModules;

    [RelayCommand]
    private async Task AddRootFolderAsync()
    {
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("BootImage.RootFolders.Picker.Title")));

        if (string.IsNullOrWhiteSpace(path) ||
            AdditionalRootFolders.Any(folder => string.Equals(folder.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AdditionalRootFolders.Add(new BootImageAdditionalFolderViewModel(path, @"\", RemoveLabel, SourceLabel, DestinationLabel, SaveRootFolders));
        SaveRootFolders();
    }

    /// <summary>
    /// Removes a folder from the additional root folders list.
    /// </summary>
    public void RemoveRootFolder(BootImageAdditionalFolderViewModel folder)
    {
        if (AdditionalRootFolders.Remove(folder))
        {
            SaveRootFolders();
        }
    }

    [RelayCommand]
    private async Task AddDriverFolderAsync()
    {
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("BootImage.Drivers.Picker.Title")));

        if (string.IsNullOrWhiteSpace(path) ||
            DriverFolders.Any(folder => string.Equals(folder.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        DriverFolders.Add(new BootImageDriverFolderViewModel(path, isEnabled: true, RemoveLabel, SaveDriverFolders));
        SaveDriverFolders();
    }

    /// <summary>
    /// Removes a folder from the driver folders list.
    /// </summary>
    public void RemoveDriverFolder(BootImageDriverFolderViewModel folder)
    {
        if (DriverFolders.Remove(folder))
        {
            SaveDriverFolders();
        }
    }

    partial void OnIncludeDellDriversChanged(bool value) => SaveGeneral(general => general with { IncludeDellDrivers = value });

    partial void OnIncludeHpDriversChanged(bool value) => SaveGeneral(general => general with { IncludeHpDrivers = value });

    partial void OnContinueOnDriverErrorChanged(bool value) => Save(current => current with { ContinueOnDriverError = value });

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
        UpdateSelectedDownloadUrl();

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
        List<PowerShellModuleSelection> modules = SelectedGalleryModules
            .Concat(SelectedLocalModules)
            .Select(module => module.Selection)
            .ToList();
        Save(current => current with { PowerShellModules = modules });
    }

    private void SaveRootFolders()
    {
        Save(current => current with { AdditionalRootFolders = AdditionalRootFolders.Select(folder => folder.ToModel()).ToList() });
    }

    private void SaveDriverFolders()
    {
        Save(current => current with { DriverFolders = DriverFolders.Select(folder => folder.ToModel()).ToList() });
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

    private void SaveGeneral(Func<GeneralSettings, GeneralSettings> transform)
    {
        if (isInitializing)
        {
            return;
        }

        configurationStateService.UpdateGeneral(transform(configurationStateService.Current.General));
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        // State changes are driven by this view model; no external refresh is required.
    }
}
