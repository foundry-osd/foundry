// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class AdkPage : Page
{
    public AdkPageViewModel ViewModel { get; }

    public AdkPage()
    {
        ViewModel = App.GetService<AdkPageViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
        Unloaded -= OnUnloaded;
    }
}
