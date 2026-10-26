// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Collects a network cache share and its account, and stays open until the owning page accepts it.</summary>
public sealed partial class NetworkCacheShareDialog : ContentDialog
{
    private readonly Func<NetworkCacheShareDialogInput, string?> trySave;

    public NetworkCacheShareDialog(IApplicationLocalizationService localization, NetworkCacheShareDialogRequest request,
        Func<NetworkCacheShareDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(request);
        this.trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        InitializeComponent();
        Title = localization.GetString(request.IsNew ? "NetworkCache.ShareDialogAddTitle" : "NetworkCache.ShareDialogEditTitle");
        PrimaryButtonText = localization.GetString(request.IsNew ? "NetworkCache.DialogAdd" : "Common.Save");
        CloseButtonText = localization.GetString("Common.Cancel");
        PathBox.Header = localization.GetString("NetworkCache.SharePath");
        PathBox.PlaceholderText = @"\\server\share";
        PathBox.Text = request.Path;
        AccountComboBox.Header = localization.GetString("NetworkCache.ShareAccount");
        AccountComboBox.ItemsSource = request.Accounts;
        AccountComboBox.SelectedItem = request.Accounts.FirstOrDefault(option => string.Equals(option.AccountId, request.AccountId, StringComparison.Ordinal))
            ?? request.Accounts.FirstOrDefault();
        AccountHintTextBlock.Text = localization.GetString("NetworkCache.ShareNoAccountHint");
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? accountId = (AccountComboBox.SelectedItem as NetworkCacheAccountOption)?.AccountId;
        string? refusal = trySave(new(PathBox.Text, accountId));
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }
}
