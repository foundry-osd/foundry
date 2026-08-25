// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Deploy.Motion;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private DeploymentPage _previousPage = DeploymentPage.Splash;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.Session.PropertyChanged += OnSessionPropertyChanged;
        Loaded += OnLoaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Session.PropertyChanged -= OnSessionPropertyChanged;
        _viewModel.Dispose();

        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeploymentSessionViewModel.CurrentPage))
        {
            return;
        }

        DeploymentPage currentPage = _viewModel.Session.CurrentPage;
        if (currentPage == DeploymentPage.Wizard)
        {
            TransitionAnimator.FadeAndTranslateY(MainContentHost, MainContentTransform, 12);
        }
        else if (IsStatusPage(currentPage) && !IsStatusPage(_previousPage))
        {
            TransitionAnimator.FadeAndTranslateY(DeploymentStatusHost, DeploymentStatusTransform, 12);
        }

        _previousPage = currentPage;
    }

    private static bool IsStatusPage(DeploymentPage page)
    {
        return page is DeploymentPage.Progress or DeploymentPage.Success or DeploymentPage.Error;
    }

}
