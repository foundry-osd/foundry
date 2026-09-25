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
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
