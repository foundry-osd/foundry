// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        DetachSession();
        if (e.NewValue is MainWindowViewModel viewModel)
        {
            AttachSession(viewModel.Session);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            AttachSession(viewModel.Session);
        }

        AnimateElement(LandingContent, LandingTransform, 14);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSession();
        LandingContent.BeginAnimation(OpacityProperty, null);
        LandingTransform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void AttachSession(DeploymentSessionViewModel session)
    {
        if (_session == session)
        {
            return;
        }

        DetachSession();
        _session = session;
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
        if (e.PropertyName == nameof(DeploymentSessionViewModel.IsStartupInitializing))
        {
            AnimateElement(LandingActionHost, LandingActionTransform, 8);
        }
    }

    private static void AnimateElement(UIElement element, TranslateTransform? transform, double offset)
    {
        element.BeginAnimation(OpacityProperty, null);
        transform?.BeginAnimation(TranslateTransform.YProperty, null);
        if (!SystemParameters.ClientAreaAnimation)
        {
            element.Opacity = 1;
            if (transform is not null)
            {
                transform.Y = 0;
            }

            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        transform?.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(offset, 0, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }
}
