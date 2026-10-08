// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

/// <summary>Authors interactive domain joining with the shared domain and OU lists.</summary>
public sealed partial class InteractiveDomainJoinPage : Page
{
    public DomainJoinConfigurationViewModel ViewModel { get; }

    public InteractiveDomainJoinPage()
    {
        ViewModel = App.GetService<DomainJoinConfigurationViewModel>();
        ViewModel.SetPageMode(DomainJoinMode.Interactive);
        InitializeComponent();
        TableViewDefaultSort.Attach(DomainsTable, nameof(DomainJoinDomainEntryViewModel.DomainName));
        TableViewFullWidth.Attach(DomainsTable);
        TableViewDefaultSort.Attach(OrganizationalUnitsTable, nameof(DomainJoinOrganizationalUnitEntryViewModel.DisplayName));
        ViewModel.DomainRowsRemoving += OnDomainRowsRemoving;
        ViewModel.OrganizationalUnitRowsRemoving += OnOrganizationalUnitRowsRemoving;
        Unloaded += OnUnloaded;
    }

    private void OnDomainRowsRemoving(object? sender, EventArgs e) => DomainsTable.DeselectAll();

    private void OnOrganizationalUnitRowsRemoving(object? sender, EventArgs e) => OrganizationalUnitsTable.DeselectAll();

    private void OrganizationalUnitsTable_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ReplaceSelectedOrganizationalUnits(OrganizationalUnitsTable.SelectedItems.OfType<DomainJoinOrganizationalUnitEntryViewModel>());


    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.DomainRowsRemoving -= OnDomainRowsRemoving;
        ViewModel.OrganizationalUnitRowsRemoving -= OnOrganizationalUnitRowsRemoving;
        ViewModel.Dispose();
    }
}
