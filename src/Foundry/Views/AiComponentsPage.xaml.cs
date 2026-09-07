// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class AiComponentsPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public AiComponentsPage()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
        ViewModel = App.GetService<PageViewModelFactory>().Create<CustomizationConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
