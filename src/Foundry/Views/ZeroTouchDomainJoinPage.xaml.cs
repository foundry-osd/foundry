// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

/// <summary>Authors automatic domain joining with shared destination selection.</summary>
public sealed partial class ZeroTouchDomainJoinPage : Page
{
    public DomainJoinConfigurationViewModel ViewModel { get; }

    public ZeroTouchDomainJoinPage()
    {
        ViewModel = App.GetService<DomainJoinConfigurationViewModel>();
        ViewModel.SetPageMode(DomainJoinMode.Automatic);
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnRemoveOuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DomainJoinOrganizationalUnitEntryViewModel row }) ViewModel.RemoveOrganizationalUnit(row);
    }

    private bool synchronizingPassword;

    private void OnPasswordLoaded(object sender, RoutedEventArgs e) => SynchronizePassword();
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!synchronizingPassword) ViewModel.SetPassword(DomainPasswordBox.Password.AsSpan());
    }
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DomainJoinConfigurationViewModel.SecretStateVersion)) SynchronizePassword();
    }
    private void SynchronizePassword()
    {
        char[]? password = ViewModel.GetPasswordCopy();
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
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
        synchronizingPassword = true; DomainPasswordBox.Password = string.Empty;
    }
}
