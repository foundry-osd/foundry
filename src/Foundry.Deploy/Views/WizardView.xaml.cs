// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Views;

public partial class WizardView : UserControl
{
    private const double CompactWidth = 880;
    private int _previousStepIndex;

    public WizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel viewModel)
        {
            _previousStepIndex = viewModel.WizardStepIndex;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.WizardStepIndex) || sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        int direction = viewModel.WizardStepIndex >= _previousStepIndex ? 1 : -1;
        _previousStepIndex = viewModel.WizardStepIndex;
        AnimateStep(direction);
    }

    private void AnimateStep(int direction)
    {
        StepContent.BeginAnimation(OpacityProperty, null);
        StepContentTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        StepContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        StepContentTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(14 * direction, 0, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < CompactWidth;
        StepperColumn.Width = isCompact ? new GridLength(0) : new GridLength(220);
        StepperGapColumn.Width = isCompact ? new GridLength(0) : new GridLength(24);
        CompactStepperRow.Height = isCompact ? GridLength.Auto : new GridLength(0);
        VerticalStepper.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        CompactStepper.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        StepContent.BeginAnimation(OpacityProperty, null);
        StepContentTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
    }
}
