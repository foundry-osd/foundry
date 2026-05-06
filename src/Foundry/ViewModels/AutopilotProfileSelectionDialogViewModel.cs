using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class AutopilotProfileSelectionDialogViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationLocalizationService localizationService;

    public AutopilotProfileSelectionDialogViewModel(
        IApplicationLocalizationService localizationService,
        IReadOnlyList<AutopilotProfileSettings> availableProfiles)
    {
        this.localizationService = localizationService;
        ArgumentNullException.ThrowIfNull(availableProfiles);

        RefreshLocalizedText();
        foreach (AutopilotProfileSettings profile in availableProfiles
                     .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entry = new SelectableAutopilotProfileEntryViewModel(profile);
            entry.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(entry);
        }

        RefreshSelectionState();
        localizationService.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<SelectableAutopilotProfileEntryViewModel> Profiles { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string SelectAllText { get; set; }

    [ObservableProperty]
    public partial string ClearText { get; set; }

    [ObservableProperty]
    public partial string ImportText { get; set; }

    [ObservableProperty]
    public partial string CancelText { get; set; }

    [ObservableProperty]
    public partial string NameColumnHeader { get; set; }

    [ObservableProperty]
    public partial string FolderColumnHeader { get; set; }

    [ObservableProperty]
    public partial string SelectedCountDisplay { get; set; }

    [ObservableProperty]
    public partial bool HasSelectedProfiles { get; set; }

    [ObservableProperty]
    public partial SelectableAutopilotProfileEntryViewModel? SelectedProfile { get; set; }

    public IReadOnlyList<AutopilotProfileSettings> GetSelectedProfiles()
    {
        return Profiles
            .Where(profile => profile.IsSelected)
            .Select(profile => profile.Profile)
            .ToArray();
    }

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        foreach (SelectableAutopilotProfileEntryViewModel profile in Profiles)
        {
            profile.PropertyChanged -= OnProfilePropertyChanged;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (SelectableAutopilotProfileEntryViewModel profile in Profiles)
        {
            profile.IsSelected = true;
        }

        RefreshSelectionState();
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (SelectableAutopilotProfileEntryViewModel profile in Profiles)
        {
            profile.IsSelected = false;
        }

        RefreshSelectionState();
    }

    private void RefreshLocalizedText()
    {
        Title = localizationService.GetString("Autopilot.TenantPickerTitle");
        Description = localizationService.GetString("Autopilot.TenantPickerDescription");
        SelectAllText = localizationService.GetString("Autopilot.TenantPickerSelectAll");
        ClearText = localizationService.GetString("Autopilot.TenantPickerClear");
        ImportText = localizationService.GetString("Autopilot.TenantPickerImport");
        CancelText = localizationService.GetString("Autopilot.TenantPickerCancel");
        NameColumnHeader = localizationService.GetString("Autopilot.ColumnName");
        FolderColumnHeader = localizationService.GetString("Autopilot.ColumnFolder");
        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        int selectedCount = Profiles.Count(profile => profile.IsSelected);
        HasSelectedProfiles = selectedCount > 0;
        SelectedCountDisplay = localizationService.FormatString(
            "Autopilot.TenantPickerSelectedCountFormat",
            selectedCount,
            Profiles.Count);
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SelectableAutopilotProfileEntryViewModel.IsSelected), StringComparison.Ordinal))
        {
            RefreshSelectionState();
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
    }

    partial void OnSelectedProfileChanged(SelectableAutopilotProfileEntryViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        value.IsSelected = !value.IsSelected;
        SelectedProfile = null;
    }
}

public sealed partial class SelectableAutopilotProfileEntryViewModel : ObservableObject
{
    public SelectableAutopilotProfileEntryViewModel(AutopilotProfileSettings profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        IsSelected = true;
    }

    public AutopilotProfileSettings Profile { get; }
    public string DisplayName => Profile.DisplayName;
    public string FolderName => Profile.FolderName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
