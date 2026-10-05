// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Backs the picker shown after searching the authoring computer's domain for organizational units.</summary>
public sealed partial class DomainJoinOuSelectionDialogViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationLocalizationService localization;

    public DomainJoinOuSelectionDialogViewModel(IApplicationLocalizationService localization,
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates, bool isIncomplete)
    {
        this.localization = localization;
        ArgumentNullException.ThrowIfNull(candidates);

        Title = localization.GetString("DomainJoinDiscovery.Header");
        Description = localization.GetString("DomainJoinDiscovery.Description");
        IncompleteText = isIncomplete ? localization.GetString("DomainJoin.DiscoveryIncomplete") : string.Empty;
        SelectAllText = localization.GetString("Autopilot.TenantPickerSelectAll");
        ClearText = localization.GetString("Autopilot.TenantPickerClear");
        AddText = localization.GetString("DomainJoinImportSelected.Content");
        CancelText = localization.GetString("Common.Cancel");
        foreach (DomainJoinOrganizationalUnitSettings unit in candidates.OrderBy(unit => unit.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var entry = new SelectableDomainJoinOuEntryViewModel(unit);
            entry.PropertyChanged += OnEntryPropertyChanged;
            OrganizationalUnits.Add(entry);
        }

        RefreshSelectionState();
    }

    /// <summary>Gets the discovered OUs with per-row selection state.</summary>
    public ObservableCollection<SelectableDomainJoinOuEntryViewModel> OrganizationalUnits { get; } = [];
    public string Title { get; }
    public string Description { get; }
    /// <summary>Gets the notice shown when the directory returned only part of its OUs; empty otherwise.</summary>
    public string IncompleteText { get; }
    public Visibility IncompleteVisibility => IncompleteText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectAllText { get; }
    public string ClearText { get; }
    public string AddText { get; }
    public string CancelText { get; }

    [ObservableProperty]
    public partial string SelectedCountDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    /// <summary>Gets the OUs checked by the user.</summary>
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> GetSelected() =>
        OrganizationalUnits.Where(entry => entry.IsSelected).Select(entry => entry.Settings).ToArray();

    /// <summary>Releases row selection subscriptions.</summary>
    public void Dispose()
    {
        foreach (SelectableDomainJoinOuEntryViewModel entry in OrganizationalUnits)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
        }
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void Clear() => SetAll(false);

    private void SetAll(bool isSelected)
    {
        foreach (SelectableDomainJoinOuEntryViewModel entry in OrganizationalUnits)
        {
            entry.IsSelected = isSelected;
        }
    }

    private void RefreshSelectionState()
    {
        int selectedCount = OrganizationalUnits.Count(entry => entry.IsSelected);
        HasSelection = selectedCount > 0;
        SelectedCountDisplay = localization.FormatString("DomainJoin.SelectedCountFormat", selectedCount, OrganizationalUnits.Count);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SelectableDomainJoinOuEntryViewModel.IsSelected), StringComparison.Ordinal))
        {
            RefreshSelectionState();
        }
    }
}

/// <summary>
/// Wraps a discovered OU with dialog selection state. Rows start unchecked because a domain lists OUs, such as
/// Domain Controllers, that must not receive deployed computers by default.
/// </summary>
public sealed partial class SelectableDomainJoinOuEntryViewModel(DomainJoinOrganizationalUnitSettings settings) : ObservableObject
{
    public DomainJoinOrganizationalUnitSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));
    public string DisplayName => Settings.DisplayName;
    public string DistinguishedName => Settings.DistinguishedName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
