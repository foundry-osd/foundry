// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

/// <summary>Authors interactive domain joining with the shared OU list.</summary>
public sealed partial class InteractiveDomainJoinPage : Page
{
    public DomainJoinConfigurationViewModel ViewModel { get; }

    public InteractiveDomainJoinPage()
    {
        ViewModel = App.GetService<DomainJoinConfigurationViewModel>();
        ViewModel.SetPageMode(DomainJoinMode.Interactive);
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void CatalogTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedListedRows(tableView.SelectedItems.OfType<DomainJoinOrganizationalUnitEntryViewModel>());
        }
    }


    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
