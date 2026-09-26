// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class CustomImagesPage : Page
{
    public CustomImagesViewModel ViewModel { get; }

    public CustomImagesPage()
    {
        ViewModel = App.GetService<CustomImagesViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEdit || ViewModel.SelectedImage is not { } image) return;
        ViewModel.RenameText = image.Name;
        ViewModel.StatusMessage = string.Empty;
        var dialog = new CustomImageEditDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
