// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.DomainJoin;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Collects a domain and, in Zero-touch, its join account, and stays open until the owning page accepts it.</summary>
public sealed partial class DomainJoinDomainDialog : ContentDialog
{
    private const int SharedAccountIndex = 0;
    private const int DedicatedAccountIndex = 1;
    private readonly DomainJoinDomainDialogRequest request;
    private readonly Func<DomainJoinDomainDialogInput, string?> trySave;

    public DomainJoinDomainDialog(IApplicationLocalizationService localization, DomainJoinDomainDialogRequest request,
        Func<DomainJoinDomainDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(localization);
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        InitializeComponent();
        Title = localization.GetString(request.IsNew ? "DomainJoin.DomainDialogAddTitle" : "DomainJoin.DomainDialogEditTitle");
        PrimaryButtonText = localization.GetString(request.IsNew ? "DomainJoin.DomainDialogAdd" : "DomainJoin.DomainDialogSave");
        CloseButtonText = localization.GetString("Common.Cancel");
        DomainNameBox.Text = request.DomainName;
        DomainNameBox.IsEnabled = request.CanRename;
        RenameHintTextBlock.Text = localization.GetString("DomainJoin.DomainRenameBlocked");
        RenameHintTextBlock.Visibility = request.CanRename ? Visibility.Collapsed : Visibility.Visible;
        AccountPanel.Visibility = request.AsksForAccount ? Visibility.Visible : Visibility.Collapsed;
        AccountBox.Text = request.AccountName ?? string.Empty;
        // Editing keeps the stored password unless a new one is typed; a new dedicated account needs one.
        PasswordHintTextBlock.Text = localization.GetString("DomainJoin.DomainDialogPasswordHint");
        PasswordHintTextBlock.Visibility = request.IsNew || request.AccountName is null ? Visibility.Collapsed : Visibility.Visible;
        AccountChoice.SelectedIndex = request.AccountName is null ? SharedAccountIndex : DedicatedAccountIndex;
        DedicatedPanel.Visibility = request.AccountName is null ? Visibility.Collapsed : Visibility.Visible;
        Closed += OnClosed;
    }

    private void OnAccountChoiceChanged(object sender, SelectionChangedEventArgs e) =>
        DedicatedPanel.Visibility = AccountChoice.SelectedIndex == DedicatedAccountIndex ? Visibility.Visible : Visibility.Collapsed;

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        bool usesSharedAccount = !request.AsksForAccount || AccountChoice.SelectedIndex != DedicatedAccountIndex;
        string? refusal = trySave(new(DomainNameBox.Text, usesSharedAccount, AccountBox.Text,
            usesSharedAccount ? ReadOnlyMemory<char>.Empty : PasswordBox.Password.AsMemory()));
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Closed -= OnClosed;
        PasswordBox.Password = string.Empty;
    }
}
