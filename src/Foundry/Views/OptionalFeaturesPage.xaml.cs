// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Foundry.Views;

public sealed partial class OptionalFeaturesPage : Page
{
    private DispatcherQueueTimer? searchTimer;

    public CustomizationConfigurationViewModel ViewModel { get; }

    public OptionalFeaturesPage()
    {
        ViewModel = App.GetService<CustomizationConfigurationViewModel>();
        InitializeComponent();
        FeatureSearchBox.Text = ViewModel.WindowsOptionalFeatureSearchText;
        searchTimer = DispatcherQueue.CreateTimer();
        searchTimer.Interval = TimeSpan.FromMilliseconds(300);
        searchTimer.IsRepeating = false;
        searchTimer.Tick += OnSearchTimerTick;
        Unloaded += OnUnloaded;
    }

    private void OnFeatureSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        searchTimer?.Stop();
        searchTimer?.Start();
    }

    private void OnSearchTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ViewModel.WindowsOptionalFeatureSearchText = FeatureSearchBox.Text;
    }

    private void OnFeatureTreeRowTapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is not FrameworkElement row
            || IsFeatureStateButtonInteraction(args.OriginalSource as DependencyObject, row))
        {
            return;
        }

        if (row is
            {
                DataContext: WindowsOptionalFeatureTreeNodeViewModel { Children.Count: > 0 } node
            })
        {
            node.IsExpanded = !node.IsExpanded;
            args.Handled = true;
        }
    }

    private static bool IsFeatureStateButtonInteraction(
        DependencyObject? source,
        DependencyObject row)
    {
        for (var current = source; current is not null && current != row; current = VisualTreeHelper.GetParent(current))
        {
            if (current is DropDownButton)
            {
                return true;
            }
        }

        return false;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        if (searchTimer is not null)
        {
            searchTimer.Stop();
            searchTimer.Tick -= OnSearchTimerTick;
            searchTimer = null;
        }

        ViewModel.Dispose();
    }
}
