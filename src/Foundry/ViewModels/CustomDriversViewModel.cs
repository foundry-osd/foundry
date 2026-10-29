// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Services.Application;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Manages the custom driver sets of the configuration: folder imports into the local driver library, labels, inclusion and the default set.
/// Setting a default includes the set, and excluding or removing the default set clears the default.
/// </summary>
public sealed partial class CustomDriversViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService state;
    private readonly CustomDriverSetLibrary library;
    private readonly CustomDriverSetImportDialogService importDialog;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly ContentPackageCleanupService cleanup;
    private readonly Dictionary<string, int?> infCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> requestedInspections = new(StringComparer.OrdinalIgnoreCase);
    private bool applying;
    private bool disposed;
    private bool libraryStateLoaded;
    private bool inspectingUnknown;

    public CustomDriversViewModel(IFoundryConfigurationStateService state, CustomDriverSetLibrary library, CustomDriverSetImportDialogService importDialog,
        IDialogService dialogs, IApplicationLocalizationService localization, ContentPackageCleanupService cleanup)
    {
        this.state = state;
        this.library = library;
        this.importDialog = importDialog;
        this.dialogs = dialogs;
        this.localization = localization;
        this.cleanup = cleanup;
        state.StateChanged += OnStateChanged;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyState();
    }

    public ObservableCollection<CustomDriverSetRow> Sets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct), nameof(CanEdit), nameof(CanRename), nameof(CanClearDefault))]
    public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial CustomDriverSetRow? SelectedSet { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename))]
    public partial string RenameText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle), nameof(CanAct), nameof(CanEdit), nameof(CanRename), nameof(CanClearDefault))]
    public partial bool IsBusy { get; set; }

    public bool CanToggle => !IsBusy && !disposed;
    public bool CanAct => IsEnabled && CanToggle;
    public bool HasSelection => SelectedSet is not null;
    public bool HasSets => Sets.Count > 0;
    public bool IsEmpty => Sets.Count == 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanEdit => CanAct && HasSelection;
    public bool CanRename => CanEdit && RenameText.Trim().Length is > 0 and <= CustomDriverSetsSettingsValidator.MaximumDisplayNameLength && !RenameText.Any(char.IsControl);
    public bool CanClearDefault => CanAct && state.Current.CustomDrivers.DefaultSetId is not null;
    public int MaximumNameLength => CustomDriverSetsSettingsValidator.MaximumDisplayNameLength;
    public bool HasReadinessIssue => !state.IsCustomDriversReady;
    public string ReadinessMessage
    {
        get
        {
            if (!state.Current.CustomDrivers.Sets.Any(set => set.IsIncluded)) return Text("ReadinessNoIncludedSet");
            CustomDriverSetRow? missing = Sets.FirstOrDefault(row => row.IsIncluded && row.IsAvailable == false);
            return missing is not null ? localization.FormatString("CustomDrivers.ReadinessMissingContent", missing.Name) : Text("ReadinessInvalid");
        }
    }
    public string DocumentationUrl => FoundryApplicationInfo.DocumentationUrl + "/foundry-osd/customization/custom-drivers";
    public string PageTitle => localization.GetString("Nav_CustomDriversKey.Title");
    public string PageDescription => localization.GetString("Nav_CustomDriversKey.Description");
    /// <summary>Gets the PXE warning, empty when the network cache serves imported content to PXE starts.</summary>
    public string PxeWarning => PxeMissingContent.IsServedByNetworkCache(state.Current) ? string.Empty : Text("PxeWarning");
    public string EnableLabel => Text("EnableLabel");
    public string ImportLabel => Text("ImportLabel");
    public string EmptyMessage => Text("EmptyMessage");
    public string LibraryLabel => Text("LibraryLabel");
    public string SetDefaultLabel => Text("SetDefaultLabel");
    public string DefaultLabel => Text("DefaultLabel");
    public string InfCountLabel => Text("InfCountLabel");
    public string FileCountLabel => Text("FileCountLabel");
    public string RefreshLabel => localization.GetString("CustomImages.RefreshLabel");
    public string RenameLabel => localization.GetString("CustomImages.RenameLabel");
    public string RemoveLabel => localization.GetString("CustomImages.RemoveLabel");
    public string ClearDefaultLabel => localization.GetString("CustomImages.ClearDefaultActionLabel");
    public string NameLabel => localization.GetString("CustomImages.NameLabel");
    public string SizeLabel => localization.GetString("CustomImages.SizeLabel");
    public string IncludedLabel => localization.GetString("CustomImages.IncludedLabel");
    public string StatusLabel => localization.GetString("CustomImages.StatusLabel");
    public string SaveLabel => localization.GetString("Common.Save");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string IncludeLabel => localization.GetString(SelectedSet?.IsIncluded == true ? "CustomImages.ExcludeActionLabel" : "CustomImages.IncludeActionLabel");
    public Symbol IncludeSymbol => SelectedSet?.IsIncluded == true ? Symbol.Cancel : Symbol.Accept;
    private string Text(string key) => localization.GetString("CustomDrivers." + key);

    partial void OnIsEnabledChanged(bool value)
    {
        if (!applying) state.UpdateCustomDrivers(state.Current.CustomDrivers with { IsEnabled = value });
    }

    partial void OnSelectedSetChanged(CustomDriverSetRow? value)
    {
        RenameText = value?.Name ?? string.Empty;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(IncludeLabel));
        OnPropertyChanged(nameof(IncludeSymbol));
    }

    /// <summary>Re-checks library availability (state service cache) and INF counts off the UI thread, then re-renders the table.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || disposed) return;
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            await state.RefreshCustomDriversReadinessAsync();
            if (disposed) return;
            infCounts.Clear();
            await LoadInfCountsAsync();
            if (disposed) return;
            libraryStateLoaded = true;
            ApplyState();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Custom driver set library state could not be refreshed.");
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Opens the import dialog, which owns the copy, its progress and import failures. The new set is added only when the
    /// configuration was not replaced while the dialog was open.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanAct) return;
        StatusMessage = string.Empty;
        if (state.Current.CustomDrivers.Sets.Count >= CustomDriverSetsSettingsValidator.MaximumSets)
        {
            StatusMessage = Text("SetLimitReached");
            return;
        }

        IsBusy = true;
        long replacementVersion = state.ReplacementVersion;
        try
        {
            CustomDriverSetImportResult? imported;
            string? existingSetId;
            try { (imported, existingSetId) = await importDialog.ShowAsync(); }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Custom driver set import dialog failed.");
                if (!disposed) StatusMessage = Text("ImportFailed");
                return;
            }
            if (disposed) return;
            if (imported is null)
            {
                if (existingSetId is not null && state.ReplacementVersion == replacementVersion)
                    SelectedSet = Sets.FirstOrDefault(row => row.Reference.Id == existingSetId);
                return;
            }
            infCounts[imported.Package.ContentHash] = imported.InfCount;
            if (state.ReplacementVersion != replacementVersion)
            {
                Logger.Information("Custom driver set import not added because the configuration was replaced. ContentHash={ContentHash}", imported.Package.ContentHash);
                StatusMessage = Text("ConfigurationReplaced");
                return;
            }
            try { AddImportedSet(imported); }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Adding the imported custom driver set failed");
                if (!disposed) StatusMessage = Text("ImportFailed");
            }
        }
        finally { IsBusy = false; }
    }

    private void AddImportedSet(CustomDriverSetImportResult imported)
    {
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        if (settings.Sets.Count >= CustomDriverSetsSettingsValidator.MaximumSets)
        {
            StatusMessage = Text("SetLimitReached");
            return;
        }

        ContentPackageReference package = imported.Package;
        var set = new CustomDriverSetReference
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = imported.Name,
            // A drive root has no folder name; the package label must stay non-empty to remain a valid reference.
            Package = string.IsNullOrWhiteSpace(package.DisplayName) ? package with { DisplayName = imported.Name } : package,
            IsIncluded = true
        };
        state.UpdateCustomDrivers(settings with { Sets = settings.Sets.Append(set).ToArray() });
        SelectedSet = Sets.FirstOrDefault(row => row.Reference.Id == set.Id);
    }

    [RelayCommand]
    private void ToggleIncluded()
    {
        if (!CanEdit || SelectedSet is not { } row) return;
        StatusMessage = string.Empty;
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        bool include = !row.IsIncluded;
        state.UpdateCustomDrivers(settings with
        {
            Sets = settings.Sets.Select(set => set.Id == row.Reference.Id ? set with { IsIncluded = include } : set).ToArray(),
            DefaultSetId = !include && settings.DefaultSetId == row.Reference.Id ? null : settings.DefaultSetId
        });
    }

    /// <summary>Renames the selected set; the dialog keeps itself open while <see cref="HasStatus"/> reports a conflict.</summary>
    [RelayCommand]
    private void Rename()
    {
        if (!CanRename || SelectedSet is not { } row) return;
        string name = RenameText.Trim();
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        if (settings.Sets.Any(set => set.Id != row.Reference.Id && set.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = Text("DuplicateName");
            return;
        }
        StatusMessage = string.Empty;
        state.UpdateCustomDrivers(settings with
        {
            Sets = settings.Sets.Select(set => set.Id == row.Reference.Id ? set with { DisplayName = name } : set).ToArray()
        });
    }

    [RelayCommand]
    private void SetDefault()
    {
        if (!CanEdit || SelectedSet is not { } row) return;
        StatusMessage = string.Empty;
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        state.UpdateCustomDrivers(settings with
        {
            Sets = settings.Sets.Select(set => set.Id == row.Reference.Id ? set with { IsIncluded = true } : set).ToArray(),
            DefaultSetId = row.Reference.Id
        });
    }

    [RelayCommand]
    private void ClearDefault()
    {
        if (!CanClearDefault) return;
        StatusMessage = string.Empty;
        state.UpdateCustomDrivers(state.Current.CustomDrivers with { DefaultSetId = null });
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!CanEdit || SelectedSet is not { } row) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        try
        {
            if (!await dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                Text("RemoveTitle"),
                DialogText.JoinParagraphs(localization.FormatString("CustomDrivers.RemoveConfirmationFormat", row.Name, row.Size), Text("RemoveWarning")),
                Text("RemoveConfirm"),
                CancelLabel,
                PreferCancel: true)) ||
                disposed || !ReferenceEquals(settings, state.Current.CustomDrivers)) return;
            bool cleaned = await cleanup.RemoveCustomDriverSetAsync(settings, row.Reference.Id);
            if (!disposed && !cleaned) StatusMessage = Text("ContentCleanupFailed");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Custom driver set could not be removed. SetId={SetId}", row.Reference.Id);
            if (!disposed) StatusMessage = Text("RemoveFailed");
        }
        finally { IsBusy = false; }
    }

    private static ContentPackageReference[] DistinctPackages(CustomDriverSetsSettings settings) =>
        settings.Sets.Select(set => set.Package).DistinctBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Reads the INF count of available packages without a count yet; the manifest read runs off the UI thread.</summary>
    private async Task LoadInfCountsAsync()
    {
        ContentPackageReference[] packages = DistinctPackages(state.Current.CustomDrivers)
            .Where(package => state.GetCustomDriverSetAvailability(package.ContentHash) == true && !infCounts.ContainsKey(package.ContentHash))
            .ToArray();
        if (packages.Length == 0) return;
        var counts = await Task.Run(() => packages.Select(package => (package.ContentHash, Count: library.TryCountInfFiles(package.ContentHash))).ToArray());
        foreach ((string hash, int? count) in counts) infCounts[hash] = count;
    }

    /// <summary>
    /// Fills the availability cache and INF counts for sets that appeared after the initial load (configuration replacement, import).
    /// Each package is requested once per page so a failing check cannot loop.
    /// </summary>
    private async Task InspectUnknownPackagesAsync()
    {
        if (inspectingUnknown || disposed) return;
        inspectingUnknown = true;
        try
        {
            if (DistinctPackages(state.Current.CustomDrivers).Any(package =>
                state.GetCustomDriverSetAvailability(package.ContentHash) is null && requestedInspections.Add(package.ContentHash)))
                await state.RefreshCustomDriversReadinessAsync();
            if (disposed) return;
            await LoadInfCountsAsync();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Custom driver set library state could not be inspected.");
            return;
        }
        finally { inspectingUnknown = false; }
        ApplyState();
    }

    private void ApplyState()
    {
        if (disposed) return;
        applying = true;
        try
        {
            string? selectedId = SelectedSet?.Reference.Id;
            CustomDriverSetsSettings settings = state.Current.CustomDrivers;
            IsEnabled = settings.IsEnabled;
            SelectedSet = null;
            Sets.Clear();
            bool hasUnknownState = false;
            foreach (CustomDriverSetReference set in settings.Sets)
            {
                // Availability comes from the state service cache; sets not checked yet render as "checking" without touching the filesystem.
                string hash = set.Package.ContentHash;
                bool? isAvailable = state.GetCustomDriverSetAvailability(hash);
                int? infCount = isAvailable == true && infCounts.TryGetValue(hash, out int? count) ? count : null;
                hasUnknownState |= (isAvailable is null && !requestedInspections.Contains(hash)) || (isAvailable == true && !infCounts.ContainsKey(hash));
                bool isDefault = settings.DefaultSetId == set.Id;
                string status = isAvailable is null ? Text("StatusChecking") :
                    localization.GetString(isAvailable.Value ? "CustomImages.Available" : "CustomImages.Missing");
                Sets.Add(new(set, infCount, ByteSizeFormatter.Format(localization, (ulong)set.Package.Length),
                    localization.GetString(set.IsIncluded ? "CustomImages.Yes" : "CustomImages.No"),
                    localization.GetString(isDefault ? "CustomImages.Yes" : "CustomImages.No"),
                    status, isDefault, isAvailable));
            }
            SelectedSet = Sets.FirstOrDefault(row => row.Reference.Id == selectedId);
            // The initial load is handled by RefreshAsync on Loaded; sets that appear later are inspected here.
            if (hasUnknownState && libraryStateLoaded) _ = InspectUnknownPackagesAsync();
            OnPropertyChanged(nameof(HasReadinessIssue));
            OnPropertyChanged(nameof(PxeWarning));
            OnPropertyChanged(nameof(ReadinessMessage));
            OnPropertyChanged(nameof(HasSets));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanClearDefault));
        }
        finally { applying = false; }
    }

    private void OnStateChanged(object? sender, EventArgs e) => ApplyState();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        ApplyState();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        state.StateChanged -= OnStateChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

/// <summary>Displays one driver set of the configuration with its library state.</summary>
public sealed record CustomDriverSetRow(CustomDriverSetReference Reference, int? InfCount, string Size, string Included, string Default,
    string Status, bool IsDefault, bool? IsAvailable)
{
    public string Name => Reference.DisplayName;
    public bool IsIncluded => Reference.IsIncluded;
    public int FileCount => Reference.Package.FileCount;
    public long Length => Reference.Package.Length;
    public Style IncludedStyle => (Style)Application.Current.Resources[IsIncluded ? "FoundrySuccessTextBlockStyle" : "FoundrySecondaryTextBlockStyle"];
    public Style DefaultStyle => (Style)Application.Current.Resources[IsDefault ? "FoundrySuccessTextBlockStyle" : "FoundrySecondaryTextBlockStyle"];
    public Style StatusStyle => (Style)Application.Current.Resources[IsAvailable switch
    {
        true => "FoundrySuccessTextBlockStyle",
        false => "FoundryCriticalTextBlockStyle",
        null => "FoundrySecondaryTextBlockStyle"
    }];
}
