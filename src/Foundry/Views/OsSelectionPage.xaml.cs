// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Views;

public sealed partial class OsSelectionPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public OsSelectionPage()
    {
        ViewModel = App.GetService<CustomizationConfigurationViewModel>();
        ViewModel.InitializeSection(ConfigurationNavigationTarget.OperatingSystemSelection);
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
