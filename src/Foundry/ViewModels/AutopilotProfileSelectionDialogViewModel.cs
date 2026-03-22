using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class AutopilotProfileSelectionDialogViewModel : LocalizedViewModelBase
{
    public AutopilotProfileSelectionDialogViewModel(
        ILocalizationService localizationService,
        IReadOnlyList<AutopilotProfileSettings> availableProfiles)
        : base(localizationService)
    {
        ArgumentNullException.ThrowIfNull(availableProfiles);

        foreach (AutopilotProfileSettings profile in availableProfiles
                     .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entry = new SelectableAutopilotProfileEntry(profile);
            entry.PropertyChanged += OnProfilePropertyChanged;
            Profiles.Add(entry);
        }
    }

    public ObservableCollection<SelectableAutopilotProfileEntry> Profiles { get; } = [];

    public string SelectedCountDisplay => string.Format(
        Strings["AutopilotTenantPickerSelectedCountFormat"],
        Profiles.Count(profile => profile.IsSelected),
        Profiles.Count);

    public event EventHandler<bool?>? CloseRequested;

    public IReadOnlyList<AutopilotProfileSettings> GetSelectedProfiles()
    {
        return Profiles
            .Where(profile => profile.IsSelected)
            .Select(profile => profile.Profile)
            .ToArray();
    }

    public override void Dispose()
    {
        foreach (SelectableAutopilotProfileEntry profile in Profiles)
        {
            profile.PropertyChanged -= OnProfilePropertyChanged;
        }

        base.Dispose();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (SelectableAutopilotProfileEntry profile in Profiles)
        {
            profile.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (SelectableAutopilotProfileEntry profile in Profiles)
        {
            profile.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImportSelectedProfiles))]
    private void ImportSelectedProfiles()
    {
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    private bool CanImportSelectedProfiles()
    {
        return Profiles.Any(profile => profile.IsSelected);
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SelectableAutopilotProfileEntry.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCountDisplay));
        ImportSelectedProfilesCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class SelectableAutopilotProfileEntry : ObservableObject
{
    public SelectableAutopilotProfileEntry(AutopilotProfileSettings profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        isSelected = true;
    }

    public AutopilotProfileSettings Profile { get; }

    public string DisplayName => Profile.DisplayName;

    public string FolderName => Profile.FolderName;

    [ObservableProperty]
    private bool isSelected;
}
