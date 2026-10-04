// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Views;

/// <summary>Adapts UI editors to disposable input without converting the password to a string.</summary>
public partial class DomainJoinDialog : Window
{
    private readonly DeployDomainJoinSettings settings;
    private readonly bool requiresCredentials;
    private DomainJoinDialogResult? result;

    public DomainJoinDialog(DeployDomainJoinSettings settings, bool requiresCredentials, ILocalizationService localization)
    {
        this.settings = settings;
        this.requiresCredentials = requiresCredentials;
        InitializeComponent();
        DataContext = localization.Strings;
        Title = localization.Strings["DomainJoin.Title"];
        DomainInput.Text = settings.DomainName ?? "";
        DomainInput.IsReadOnly = !requiresCredentials;
        CredentialsPanel.Visibility = requiresCredentials ? Visibility.Visible : Visibility.Collapsed;
        DestinationPanel.Visibility = settings.AllowOuSelectionDuringDeployment ? Visibility.Visible : Visibility.Collapsed;
        DestinationInput.ItemsSource = new[] { new DomainJoinOrganizationalUnitSettings
            { Id = "", DisplayName = localization.Strings["DomainJoin.DefaultDestination"], DistinguishedName = "" } }
            .Concat(settings.OrganizationalUnits).ToArray();
        DestinationInput.SelectedIndex = 0;
        if (settings.DefaultOuId is { } id)
            DestinationInput.SelectedItem = DestinationInput.Items.Cast<DomainJoinOrganizationalUnitSettings>()
                .FirstOrDefault(unit => string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase));
        RefreshDestinations();
        Closed += (_, _) => PasswordInput.Clear();
        Loaded += (_, _) => (requiresCredentials ? (Control)DomainInput : DestinationInput).Focus();
    }

    internal DomainJoinDialogResult? TakeResult() => Interlocked.Exchange(ref result, null);

    private void DomainInput_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshDestinations();

    private void RefreshDestinations()
    {
        if (DestinationInput is null) return;
        bool compatible = DomainJoinCredentialContext.IsValidDomainName(DomainInput.Text) &&
            string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(DomainInput.Text),
                DomainJoinCredentialContext.CanonicalizeDomainName(settings.OuCatalogDomain ?? ""), StringComparison.Ordinal);
        DestinationInput.IsEnabled = compatible;
        if (!compatible) DestinationInput.SelectedIndex = 0;
    }

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
    {
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
            result = new(DomainInput.Text, requiresCredentials ? AccountInput.Text : "", string.IsNullOrEmpty(id) ? null : id, characters);
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
