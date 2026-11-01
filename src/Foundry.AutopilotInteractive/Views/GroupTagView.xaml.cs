// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Foundry.AutopilotInteractive.ViewModels;

namespace Foundry.AutopilotInteractive.Views;

/// <summary>
/// Page 2: group tag and registration. Bound to <see cref="GroupTagViewModel"/>; the code-behind only moves the keyboard
/// focus to the custom box when the view model asks for it.
/// </summary>
public partial class GroupTagView : UserControl
{
    private GroupTagViewModel? _viewModel;

    /// <summary>Builds the page.</summary>
    public GroupTagView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CustomFocusRequested -= OnCustomFocusRequested;
        }

        _viewModel = e.NewValue as GroupTagViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CustomFocusRequested += OnCustomFocusRequested;
        }
    }

    // The page becomes visible in the same dispatcher turn; focusing after layout makes sure the box is enabled and shown.
    private void OnCustomFocusRequested(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() => CustomTextBox.Focus(), DispatcherPriority.Input);
    }
}
