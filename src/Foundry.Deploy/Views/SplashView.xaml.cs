// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Foundry.Deploy.Motion;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Views;

public partial class SplashView : UserControl
{
    private DeploymentSessionViewModel? _session;

    public SplashView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachSession((e.NewValue as MainWindowViewModel)?.Session);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            AttachSession(viewModel.Session);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSession();
        TransitionAnimator.Clear(LandingActionHost, LandingActionTransform);
    }

    private void AttachSession(DeploymentSessionViewModel? session)
    {
        if (_session == session)
        {
            return;
        }

        DetachSession();
        _session = session;
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = null;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeploymentSessionViewModel.IsStartupInitializing) &&
            sender is DeploymentSessionViewModel { IsStartupInitializing: false })
        {
            TransitionAnimator.FadeAndTranslateY(LandingActionHost, LandingActionTransform, 8);
        }
    }
}
