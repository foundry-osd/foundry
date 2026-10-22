// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Services.Application;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Manages the active profile's custom driver sets: folder imports into the local driver library, labels, inclusion and the default set.
/// Setting a default includes the set, and excluding or removing the default set clears the default.
/// </summary>
public sealed partial class CustomDriversViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService state;
    private readonly CustomDriverSetLibrary library;
    private readonly IFilePickerService picker;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly DeploymentProfileCoordinator profiles;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<string, int?> infCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> requestedInspections = new(StringComparer.OrdinalIgnoreCase);
    private bool applying;
    private bool disposed;
    private bool libraryStateLoaded;
    private bool inspectingUnknown;

    public CustomDriversViewModel(IFoundryConfigurationStateService state, CustomDriverSetLibrary library, IFilePickerService picker,
        IDialogService dialogs, IApplicationLocalizationService localization, DeploymentProfileCoordinator profiles)
    {
        this.state = state;
        this.library = library;
        this.picker = picker;
        this.dialogs = dialogs;
        this.localization = localization;
        this.profiles = profiles;
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
    [ObservableProperty] public partial bool IsImporting { get; set; }

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
    public string PxeWarning => Text("PxeWarning");
    public string EnableLabel => Text("EnableLabel");
    public string ImportLabel => Text("ImportLabel");
    public string ImportingMessage => Text("ImportingContent");
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
        // The import is bound to the profile that was active when it started; an activation during the copy must not receive it.
        Guid? profileId = profiles.Active?.LocalId;
        try
        {
            string? source = await picker.PickFolderAsync(new FolderPickerRequest(Text("ImportPickerTitle")));
            if (source is null || disposed) return;
            IsImporting = true;
            Logger.Information("Custom driver set import started.");
            CancellationToken token = cancellation.Token;
            // The library walks and hashes the whole folder; keep that work off the UI thread.
            ContentPackageReference package = await Task.Run(() => library.ImportAsync(source, token), token);
            int? infCount = await Task.Run(() => library.TryCountInfFiles(package.ContentHash), token);
            Logger.Information("Custom driver set import completed. ContentHash={ContentHash}, FileCount={FileCount}, Length={Length}, InfCount={InfCount}",
                package.ContentHash, package.FileCount, package.Length, infCount);
            if (disposed) return;
            infCounts[package.ContentHash] = infCount;
            if (profiles.Active?.LocalId != profileId)
            {
                Logger.Information("Custom driver set import not added because the active profile changed. ContentHash={ContentHash}", package.ContentHash);
                StatusMessage = Text("ProfileChanged");
                return;
            }
            AddImportedSet(package);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Information("Custom driver set import canceled.");
        }
        catch (Exception exception)
        {
            string? relativePath = exception.Data[ContentPackagePathPolicy.RelativePathDataKey] as string;
            Logger.Warning(exception, "Custom driver set import failed. ErrorCode={ErrorCode}, RelativePath={RelativePath}", exception.Message, relativePath);
            if (!disposed) StatusMessage = DescribeImportFailure(exception, relativePath);
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
        }
    }

    private void AddImportedSet(ContentPackageReference package)
    {
        CustomDriverSetsSettings settings = state.Current.CustomDrivers;
        CustomDriverSetReference? existing = settings.Sets.FirstOrDefault(set =>
            set.Package.ContentHash.Equals(package.ContentHash, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedSet = Sets.FirstOrDefault(row => row.Reference.Id == existing.Id);
            StatusMessage = localization.FormatString("CustomDrivers.AlreadyInProfile", existing.DisplayName);
            return;
        }

        if (settings.Sets.Count >= CustomDriverSetsSettingsValidator.MaximumSets)
        {
            StatusMessage = Text("SetLimitReached");
            return;
        }

        string name = CreateUniqueName(package.DisplayName, settings);
        var set = new CustomDriverSetReference
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = name,
            // A drive root has no folder name; the package label must stay non-empty to remain a valid reference.
            Package = string.IsNullOrWhiteSpace(package.DisplayName) ? package with { DisplayName = name } : package,
            IsIncluded = true
        };
        state.UpdateCustomDrivers(settings with { Sets = settings.Sets.Append(set).ToArray() });
        SelectedSet = Sets.FirstOrDefault(row => row.Reference.Id == set.Id);
    }

    /// <summary>Defaults the label to the folder name and appends a counter when the profile already uses it.</summary>
    private string CreateUniqueName(string folderName, CustomDriverSetsSettings settings)
    {
        string baseName = string.IsNullOrWhiteSpace(folderName) ? Text("DefaultSetName") : folderName.Trim();
        string name = Truncate(baseName, CustomDriverSetsSettingsValidator.MaximumDisplayNameLength);
        for (int counter = 2; settings.Sets.Any(set => set.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)); counter++)
        {
            string suffix = $" ({counter})";
            name = Truncate(baseName, CustomDriverSetsSettingsValidator.MaximumDisplayNameLength - suffix.Length).TrimEnd() + suffix;
        }
        return name;
    }

    /// <summary>Cuts to <paramref name="length"/> UTF-16 units without splitting a surrogate pair.</summary>
    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        return char.IsHighSurrogate(value[length - 1]) ? value[..(length - 1)] : value[..length];
    }

    /// <summary>Maps library error codes to actionable messages; any other failure keeps the generic message.</summary>
    private string DescribeImportFailure(Exception exception, string? relativePath) => exception.Message switch
    {
        "ContentPackage.InvalidPackagePath" when relativePath is not null => localization.FormatString("CustomDrivers.ImportInvalidPath", relativePath),
        "ContentPackage.PackageTooManyFiles" => Text("ImportTooManyFiles"),
        "ContentPackage.MissingInfFile" or "ContentPackage.EmptyPackage" => Text("ImportMissingInf"),
        "ContentPackage.PackageSourceChanged" => Text("ImportSourceChanged"),
        "ContentPackage.InsufficientPackageSpace" => Text("ImportInsufficientSpace"),
        "ContentPackage.RedirectedPackagePath" => Text("ImportRedirected"),
        _ => Text("ImportFailed")
    };

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
            bool cleaned = await profiles.RemoveCustomDriverSetAsync(settings, row.Reference.Id, library);
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
    /// Fills the availability cache and INF counts for sets that appeared after the initial load (profile switch, import).
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
        cancellation.Cancel();
        cancellation.Dispose();
    }
}

/// <summary>Displays one driver set of the active profile with its library state.</summary>
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
