// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Views;

/// <summary>Adapts UI editors to disposable input without converting the password to a string.</summary>
public partial class DomainJoinDialog : Window
{
    private readonly DeployDomainJoinSettings settings;
    private readonly bool requiresCredentials;
    private readonly ILocalizationService localization;
    private DomainJoinDialogResult? result;

    public DomainJoinDialog(DeployDomainJoinSettings settings, bool requiresCredentials, ILocalizationService localization)
    {
        this.settings = settings;
        this.requiresCredentials = requiresCredentials;
        this.localization = localization;
        InitializeComponent();
        DataContext = localization.Strings;
        Title = localization.Strings["DomainJoin.Title"];
        DomainInput.Text = settings.DomainName ?? "";
        DomainInput.IsReadOnly = !requiresCredentials;
        CredentialsPanel.Visibility = requiresCredentials ? Visibility.Visible : Visibility.Collapsed;
        DestinationInput.ItemsSource = settings.OrganizationalUnits;
        RefreshDestinations();
        Closed += (_, _) => PasswordInput.Clear();
        Loaded += (_, _) => (requiresCredentials ? (Control)DomainInput : DestinationInput).Focus();
    }

    internal DomainJoinDialogResult? TakeResult() => Interlocked.Exchange(ref result, null);

    private void DomainInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (OptionalDestinationInput is not null) OptionalDestinationInput.Clear();
        RefreshDestinations();
    }

    private void RefreshDestinations()
    {
        if (DestinationInput is null || OptionalDestinationPanel is null || DestinationError is null) return;
        bool compatible = DomainJoinPreparationService.HasCompatibleCatalog(settings, DomainInput.Text);
        DestinationPanel.Visibility = compatible && settings.AllowOuSelectionDuringDeployment ? Visibility.Visible : Visibility.Collapsed;
        OptionalDestinationPanel.Visibility = requiresCredentials && !compatible ? Visibility.Visible : Visibility.Collapsed;
        DestinationInput.IsEnabled = compatible;
        DestinationInput.SelectedItem = compatible && settings.DefaultOuId is { } id
            ? settings.OrganizationalUnits.FirstOrDefault(unit => string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;
        DestinationError.Visibility = Visibility.Collapsed;
    }

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DestinationPanel.IsVisible && DestinationInput.SelectedItem is not DomainJoinOrganizationalUnitSettings)
        {
            DestinationError.Text = localization.Strings["DomainJoin.DestinationRequired"];
            DestinationError.Visibility = Visibility.Visible;
            DestinationInput.Focus();
            return;
        }
        string? destinationDn = OptionalDestinationPanel.IsVisible && !string.IsNullOrWhiteSpace(OptionalDestinationInput.Text)
            ? OptionalDestinationInput.Text : null;
        if (destinationDn is not null && !DistinguishedNameRules.IsWithinDomain(destinationDn, DomainInput.Text))
        {
            DestinationError.Text = localization.Strings["DomainJoin.DestinationInvalid"];
            DestinationError.Visibility = Visibility.Visible;
            OptionalDestinationInput.Focus();
            return;
        }
        using var password = PasswordInput.SecurePassword;
        char[] characters = new char[requiresCredentials ? password.Length : 0];
        IntPtr plaintext = IntPtr.Zero;
        try
        {
            if (requiresCredentials)
            {
                plaintext = Marshal.SecureStringToGlobalAllocUnicode(password);
                if (characters.Length > 0) Marshal.Copy(plaintext, characters, 0, characters.Length);
            }
            string? id = (DestinationInput.SelectedItem as DomainJoinOrganizationalUnitSettings)?.Id;
            result = new(DomainInput.Text, requiresCredentials ? AccountInput.Text : "", id, characters, destinationDn);
            DialogResult = true;
        }
        finally
        {
            if (plaintext != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(plaintext);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
            PasswordInput.Clear();
        }
    }
}
