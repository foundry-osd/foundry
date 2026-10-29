// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Collects a network cache account and its password, and stays open until the owning page accepts it.</summary>
public sealed partial class NetworkCacheAccountDialog : ContentDialog
{
    private readonly Func<NetworkCacheAccountDialogInput, string?> trySave;

    public NetworkCacheAccountDialog(IApplicationLocalizationService localization, NetworkCacheAccountDialogRequest request,
        Func<NetworkCacheAccountDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(request);
        this.trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        InitializeComponent();
        Title = localization.GetString(request.IsNew ? "NetworkCache.AccountDialogAddTitle" : "NetworkCache.AccountDialogEditTitle");
        PrimaryButtonText = localization.GetString(request.IsNew ? "NetworkCache.DialogAdd" : "Common.Save");
        CloseButtonText = localization.GetString("Common.Cancel");
        UserNameBox.Header = localization.GetString("NetworkCache.AccountUserName");
        UserNameBox.PlaceholderText = localization.GetString("NetworkCache.AccountUserNamePlaceholder");
        UserNameBox.Text = request.UserName;
        PasswordBox.Header = localization.GetString("NetworkCache.AccountPassword");
        ConfirmationBox.Header = localization.GetString("NetworkCache.AccountPasswordConfirmation");
        // Editing keeps the stored password unless a new one is typed.
        PasswordHintTextBlock.Text = localization.GetString("NetworkCache.AccountPasswordKeepHint");
        PasswordHintTextBlock.Visibility = request.HasPassword ? Visibility.Visible : Visibility.Collapsed;
        if (request.PasswordOnly)
        {
            // A followed publication manages the account: only its password, kept in the local vault, can be entered.
            UserNameBox.IsEnabled = false;
            PasswordOnlyHintTextBlock.Text = localization.GetString("Sharing.PasswordOnlyHint");
            PasswordOnlyHintTextBlock.Visibility = Visibility.Visible;
        }

        Closed += OnClosed;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? refusal = trySave(new(UserNameBox.Text, PasswordBox.Password.AsMemory(), ConfirmationBox.Password.AsMemory()));
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Closed -= OnClosed;
        PasswordBox.Password = string.Empty;
        ConfirmationBox.Password = string.Empty;
    }
}
