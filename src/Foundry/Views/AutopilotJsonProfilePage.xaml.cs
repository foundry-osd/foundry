// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class AutopilotJsonProfilePage : Page
{
    public AutopilotConfigurationViewModel ViewModel { get; }

    public AutopilotJsonProfilePage()
    {
        NavigationCacheMode = NavigationCacheMode.Disabled;
        ViewModel = App.GetService<PageViewModelFactory>().Create<AutopilotConfigurationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    private async void OnModeActionClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleProvisioningModeAsync(AutopilotProvisioningMode.JsonProfile);
    }

    private void ProfilesTable_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is WinUI.TableView.TableView tableView)
        {
            ViewModel.ReplaceSelectedProfiles(tableView.SelectedItems.OfType<AutopilotProfileEntryViewModel>());
        }
    }

}
