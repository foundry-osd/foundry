// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

/// <summary>Authors automatic domain joining with the shared domain and OU lists.</summary>
public sealed partial class ZeroTouchDomainJoinPage : Page
{
    private bool synchronizingPassword;

    public DomainJoinConfigurationViewModel ViewModel { get; }

    public ZeroTouchDomainJoinPage()
    {
        ViewModel = App.GetService<DomainJoinConfigurationViewModel>();
        ViewModel.SetPageMode(DomainJoinMode.Automatic);
        InitializeComponent();
        TableViewDefaultSort.Attach(DomainsTable, nameof(DomainJoinDomainEntryViewModel.DomainName));
        TableViewDefaultSort.Attach(OrganizationalUnitsTable, nameof(DomainJoinOrganizationalUnitEntryViewModel.DisplayName));
        ViewModel.DomainRowsRemoving += OnDomainRowsRemoving;
        ViewModel.OrganizationalUnitRowsRemoving += OnOrganizationalUnitRowsRemoving;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDomainRowsRemoving(object? sender, EventArgs e) => DomainsTable.DeselectAll();

    private void OnOrganizationalUnitRowsRemoving(object? sender, EventArgs e) => OrganizationalUnitsTable.DeselectAll();

    private void OrganizationalUnitsTable_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ReplaceSelectedOrganizationalUnits(OrganizationalUnitsTable.SelectedItems.OfType<DomainJoinOrganizationalUnitEntryViewModel>());

    private void OnDomainChoiceClick(object sender, RoutedEventArgs e) =>
        ViewModel.AllowDomainSelectionDuringDeployment = sender is AppBarToggleButton { IsChecked: true };

    private void OnOuChoiceClick(object sender, RoutedEventArgs e) =>
        ViewModel.AllowOuSelectionDuringDeployment = sender is AppBarToggleButton { IsChecked: true };

    private void OnPasswordLoaded(object sender, RoutedEventArgs e) => SynchronizePassword();

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!synchronizingPassword) ViewModel.SetSharedPassword(DomainPasswordBox.Password.AsSpan());
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DomainJoinConfigurationViewModel.SecretStateVersion)) SynchronizePassword();
    }

    /// <summary>Shows the password the shared account owns, without reporting that refill as a user edit.</summary>
    private void SynchronizePassword()
    {
        char[]? password = ViewModel.GetSharedPasswordCopy();
        try
        {
            synchronizingPassword = true;
            string value = password is null ? string.Empty : new(password);
            if (!string.Equals(DomainPasswordBox.Password, value, StringComparison.Ordinal)) DomainPasswordBox.Password = value;
        }
        finally
        {
            synchronizingPassword = false;
            if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.DomainRowsRemoving -= OnDomainRowsRemoving;
        ViewModel.OrganizationalUnitRowsRemoving -= OnOrganizationalUnitRowsRemoving;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
        synchronizingPassword = true;
        DomainPasswordBox.Password = string.Empty;
    }
}
