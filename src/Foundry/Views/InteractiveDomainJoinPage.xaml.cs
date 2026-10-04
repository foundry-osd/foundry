// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

/// <summary>Authors interactive domain joining with shared destination selection.</summary>
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

    private void OnRemoveOuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DomainJoinOrganizationalUnitEntryViewModel row }) ViewModel.RemoveOrganizationalUnit(row);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;

        ViewModel.Dispose();

    }
}
