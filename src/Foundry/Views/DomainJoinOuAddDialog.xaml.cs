// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Collects one organizational unit and stays open until the owning page accepts it.</summary>
public sealed partial class DomainJoinOuAddDialog : ContentDialog
{
    private readonly Func<string, string, string?> tryAdd;

    public DomainJoinOuAddDialog(IApplicationLocalizationService localization, Func<string, string, string?> tryAdd)
    {
        ArgumentNullException.ThrowIfNull(localization);
        this.tryAdd = tryAdd ?? throw new ArgumentNullException(nameof(tryAdd));
        InitializeComponent();
        Title = localization.GetString("DomainJoinManualDestination.Header");
        DescriptionTextBlock.Text = localization.GetString("DomainJoinManualDestination.Description");
        PrimaryButtonText = localization.GetString("DomainJoinAdd.Content");
        CloseButtonText = localization.GetString("Common.Cancel");
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? refusal = tryAdd(DisplayNameBox.Text, DistinguishedNameBox.Text);
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }
}
