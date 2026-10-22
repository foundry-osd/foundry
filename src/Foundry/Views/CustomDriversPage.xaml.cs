// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class CustomDriversPage : Page
{
    public CustomDriversViewModel ViewModel { get; }

    public CustomDriversPage()
    {
        ViewModel = App.GetService<CustomDriversViewModel>();
        InitializeComponent();
        TableViewDefaultSort.Attach(SetsTable, nameof(CustomDriverSetRow.Name));
        TableViewFullWidth.Attach(SetsTable);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEdit || ViewModel.SelectedSet is not { } set) return;
        ViewModel.RenameText = set.Name;
        ViewModel.StatusMessage = string.Empty;
        var dialog = new CustomDriverSetEditDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
