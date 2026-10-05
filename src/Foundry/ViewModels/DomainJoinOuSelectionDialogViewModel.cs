// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the picker shown after searching the authoring computer's domain for organizational units. Nothing is
/// selected initially because a domain lists OUs, such as Domain Controllers, that must not receive deployed
/// computers by default.
/// </summary>
public sealed partial class DomainJoinOuSelectionDialogViewModel : ObservableObject
{
    private readonly IApplicationLocalizationService localization;
    private IReadOnlyList<DomainJoinOrganizationalUnitSettings> selected = [];

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
        DisplayNameColumnHeader = localization.GetString("DomainJoinManualLabel.Header");
        DistinguishedNameColumnHeader = localization.GetString("DomainJoinManualDn.Header");
        OrganizationalUnits = candidates
            .OrderBy(unit => unit.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .Select(unit => new DomainJoinOrganizationalUnitEntryViewModel(unit))
            .ToArray();
        ReplaceSelection([]);
    }

    /// <summary>Gets the discovered OUs in distinguished-name order.</summary>
    public IReadOnlyList<DomainJoinOrganizationalUnitEntryViewModel> OrganizationalUnits { get; }
    public string Title { get; }
    public string Description { get; }
    /// <summary>Gets the notice shown when the directory returned only part of its OUs; empty otherwise.</summary>
    public string IncompleteText { get; }
    public Visibility IncompleteVisibility => IncompleteText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectAllText { get; }
    public string ClearText { get; }
    public string AddText { get; }
    public string CancelText { get; }
    public string DisplayNameColumnHeader { get; }
    public string DistinguishedNameColumnHeader { get; }

    [ObservableProperty]
    public partial string SelectedCountDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    /// <summary>Tracks the rows selected in the table.</summary>
    public void ReplaceSelection(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selected = rows.Select(row => row.Settings).ToArray();
        HasSelection = selected.Count > 0;
        SelectedCountDisplay = localization.FormatString("DomainJoin.SelectedCountFormat", selected.Count, OrganizationalUnits.Count);
    }

    /// <summary>Gets the OUs selected by the user.</summary>
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> GetSelected() => selected;
}
