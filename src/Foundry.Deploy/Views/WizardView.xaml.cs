// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Foundry.Deploy.Motion;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Views;

public partial class WizardView : UserControl
{
    private int _previousStepIndex;

    public WizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
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
        TransitionAnimator.FadeAndTranslateX(StepContent, StepContentTransform, 14 * direction);
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

        TransitionAnimator.Clear(StepContent, StepContentTransform);
    }
}
