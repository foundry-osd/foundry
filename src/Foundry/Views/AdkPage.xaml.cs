// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class AdkPage : Page
{
    public AdkPageViewModel ViewModel { get; }

    public AdkPage()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
        ViewModel = App.GetService<PageViewModelFactory>().Create<AdkPageViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
        Unloaded -= OnUnloaded;
    }
}
