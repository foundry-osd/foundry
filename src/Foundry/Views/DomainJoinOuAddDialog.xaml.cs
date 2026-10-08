// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>
/// Collects one organizational unit, or a new display name for a listed one, and stays open until the owning page
/// accepts it.
/// </summary>
public sealed partial class DomainJoinOuAddDialog : ContentDialog
{
    private readonly Func<string, string, string?> tryAdd;

    /// <param name="localization">Supplies the dialog texts.</param>
    /// <param name="tryAdd">Saves the display name and distinguished name; returns the refusal reason, or null.</param>
    /// <param name="existing">The listed OU being renamed; <see langword="null"/> adds a new OU.</param>
    public DomainJoinOuAddDialog(IApplicationLocalizationService localization, Func<string, string, string?> tryAdd,
        DomainJoinOrganizationalUnitSettings? existing = null)
    {
        ArgumentNullException.ThrowIfNull(localization);
        this.tryAdd = tryAdd ?? throw new ArgumentNullException(nameof(tryAdd));
        InitializeComponent();
        CloseButtonText = localization.GetString("Common.Cancel");
        if (existing is null)
        {
            Title = localization.GetString("DomainJoinManualDestination.Header");
            DescriptionTextBlock.Text = localization.GetString("DomainJoinManualDestination.Description");
            PrimaryButtonText = localization.GetString("DomainJoinAdd.Content");
            return;
        }

        // Only the display name changes; the distinguished name identifies the OU and is shown for reference.
        Title = localization.GetString("DomainJoin.CommandEdit");
        DescriptionTextBlock.Visibility = Visibility.Collapsed;
        PrimaryButtonText = localization.GetString("DomainJoin.DomainDialogSave");
        DisplayNameBox.Text = existing.DisplayName;
        DisplayNameBox.SelectAll();
        DistinguishedNameBox.Text = existing.DistinguishedName;
        DistinguishedNameBox.IsReadOnly = true;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? refusal = tryAdd(DisplayNameBox.Text, DistinguishedNameBox.Text);
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }
}
