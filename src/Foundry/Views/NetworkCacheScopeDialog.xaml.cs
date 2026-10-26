// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>
/// Collects an IP scope and the ordered shares a device inside it tries, and stays open until the owning page accepts
/// it. The list order is the try order; only checked shares are saved.
/// </summary>
public sealed partial class NetworkCacheScopeDialog : ContentDialog
{
    private readonly IApplicationLocalizationService localization;
    private readonly Func<NetworkCacheScopeDialogInput, string?> trySave;
    private readonly ObservableCollection<NetworkCacheScopeShareRowViewModel> shares = [];

    public NetworkCacheScopeDialog(IApplicationLocalizationService localization, NetworkCacheScopeDialogRequest request,
        Func<NetworkCacheScopeDialogInput, string?> trySave)
    {
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        ArgumentNullException.ThrowIfNull(request);
        this.trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        InitializeComponent();
        Title = localization.GetString(request.IsNew ? "NetworkCache.ScopeDialogAddTitle" : "NetworkCache.ScopeDialogEditTitle");
        PrimaryButtonText = localization.GetString(request.IsNew ? "NetworkCache.DialogAdd" : "Common.Save");
        CloseButtonText = localization.GetString("Common.Cancel");
        NameBox.Header = localization.GetString("NetworkCache.ScopeName");
        NameBox.Text = request.Name;
        ValueBox.Header = localization.GetString("NetworkCache.ScopeValue");
        ValueBox.Text = request.Value;
        KindComboBox.Header = localization.GetString("NetworkCache.ScopeKindHeader");
        SelectionOption<NetworkCacheScopeKind>[] kinds =
        [
            new(NetworkCacheScopeKind.Subnet, localization.GetString("NetworkCache.ScopeKind.Subnet")),
            new(NetworkCacheScopeKind.Range, localization.GetString("NetworkCache.ScopeKind.Range")),
            new(NetworkCacheScopeKind.Address, localization.GetString("NetworkCache.ScopeKind.Address"))
        ];
        KindComboBox.ItemsSource = kinds;
        KindComboBox.SelectedItem = kinds.First(option => option.Value == request.Kind);
        SharesHeaderTextBlock.Text = localization.GetString("NetworkCache.ScopeShares");
        SharesHintTextBlock.Text = localization.GetString("NetworkCache.ScopeSharesHint");
        NoSharesTextBlock.Text = localization.GetString("NetworkCache.ScopeNoShares");
        NoSharesTextBlock.Visibility = request.Shares.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string moveUp = localization.GetString("NetworkCache.MoveUp");
        string moveDown = localization.GetString("NetworkCache.MoveDown");
        foreach (NetworkCacheScopeShareOption option in request.Shares)
        {
            shares.Add(new(option.ShareId, option.Path, option.IsIncluded, moveUp, moveDown));
        }

        RefreshMoveStates();
        SharesList.ItemsSource = shares;
    }

    private SelectionOption<NetworkCacheScopeKind>? SelectedKind => KindComboBox.SelectedItem as SelectionOption<NetworkCacheScopeKind>;

    /// <summary>Shows an example of the selected type, so the expected syntax is visible before typing.</summary>
    private void OnKindChanged(object sender, SelectionChangedEventArgs e) =>
        ValueBox.PlaceholderText = SelectedKind?.Value switch
        {
            NetworkCacheScopeKind.Range => localization.GetString("NetworkCache.ScopeValuePlaceholder.Range"),
            NetworkCacheScopeKind.Address => localization.GetString("NetworkCache.ScopeValuePlaceholder.Address"),
            _ => localization.GetString("NetworkCache.ScopeValuePlaceholder.Subnet")
        };

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int offset)
    {
        if (sender is not FrameworkElement { DataContext: NetworkCacheScopeShareRowViewModel row }) return;
        int index = shares.IndexOf(row);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= shares.Count) return;
        shares.Move(index, target);
        RefreshMoveStates();
    }

    private void RefreshMoveStates()
    {
        for (int index = 0; index < shares.Count; index++)
        {
            shares[index].CanMoveUp = index > 0;
            shares[index].CanMoveDown = index < shares.Count - 1;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string[] shareIds = shares.Where(row => row.IsIncluded).Select(row => row.ShareId).ToArray();
        string? refusal = trySave(new(NameBox.Text, SelectedKind?.Value ?? NetworkCacheScopeKind.Subnet, ValueBox.Text, shareIds));
        ValidationTextBlock.Text = refusal ?? string.Empty;
        ValidationTextBlock.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        args.Cancel = refusal is not null;
    }
}
