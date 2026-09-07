// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class MachineNamingPage : Page
{
    public CustomizationConfigurationViewModel ViewModel { get; }

    public MachineNamingPage()
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

    private void AddComponentButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddMachineNameComponent();
    }

    private void MoveComponentUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MachineNameComponentRowViewModel row })
        {
            ViewModel.MoveMachineNameComponent(row, -1);
        }
    }

    private void MoveComponentDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MachineNameComponentRowViewModel row })
        {
            ViewModel.MoveMachineNameComponent(row, 1);
        }
    }

    private void RemoveComponentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MachineNameComponentRowViewModel row })
        {
            ViewModel.RemoveMachineNameComponent(row);
        }
    }
}
