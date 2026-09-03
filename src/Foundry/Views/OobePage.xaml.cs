// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Views;

public sealed partial class OobePage : Page
{
    private bool isSynchronizingAdministratorPasswordBoxes;

    public CustomizationConfigurationViewModel ViewModel { get; }

    public OobePage()
    {
        ViewModel = App.GetService<CustomizationConfigurationViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void AdministratorAccountToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!AdministratorAccountToggle.IsOn)
        {
            ClearAdministratorPasswordBoxes();
        }
    }

    private void AdministratorPasswordToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (AdministratorPasswordToggle.IsOn && AdministratorPasswordToggle.IsLoaded)
        {
            AdministratorPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        ClearAdministratorPasswordBoxes();
    }

    private void ClearAdministratorPasswordBoxes()
    {
        AdministratorPasswordBox.Password = string.Empty;
        AdministratorConfirmationBox.Password = string.Empty;
    }

    private void AdministratorPasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        SyncAdministratorPasswordBox(AdministratorPasswordBox, ViewModel.GetOobeAdministratorPasswordCopy());
    }

    private void AdministratorConfirmationBox_Loaded(object sender, RoutedEventArgs e)
    {
        SyncAdministratorPasswordBox(AdministratorConfirmationBox, ViewModel.GetOobeAdministratorConfirmationCopy());
    }

    private void AdministratorPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (isSynchronizingAdministratorPasswordBoxes)
        {
            return;
        }

        ViewModel.SetOobeAdministratorPassword(AdministratorPasswordBox.Password);
    }

    private void AdministratorConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (isSynchronizingAdministratorPasswordBoxes)
        {
            return;
        }

        ViewModel.SetOobeAdministratorConfirmation(AdministratorConfirmationBox.Password);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(CustomizationConfigurationViewModel.OobeAccountSecretStateVersion), StringComparison.Ordinal))
        {
            return;
        }

        SyncAdministratorPasswordBox(AdministratorPasswordBox, ViewModel.GetOobeAdministratorPasswordCopy());
        SyncAdministratorPasswordBox(AdministratorConfirmationBox, ViewModel.GetOobeAdministratorConfirmationCopy());
    }

    private void SyncAdministratorPasswordBox(PasswordBox passwordBox, char[] value)
    {
        try
        {
            isSynchronizingAdministratorPasswordBoxes = true;
            string password = new(value);
            if (!string.Equals(passwordBox.Password, password, StringComparison.Ordinal))
            {
                passwordBox.Password = password;
            }
        }
        finally
        {
            isSynchronizingAdministratorPasswordBoxes = false;
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Dispose();
    }
}
