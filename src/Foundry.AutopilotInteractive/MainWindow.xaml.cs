// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Wpf.Motion;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive;

/// <summary>
/// Full-screen shell window: menu, version, page host and device footer.
/// </summary>
/// <remarks>
/// Code-behind is limited to window glue: the page transition, the Alt+F4 close confirmation, the inherited keyboard layout
/// applied on first activation and the exit code handed to the application when the window closes.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;
    private bool _isCloseAllowed;
    private bool _isConfirmingClose;
    private bool _isClosed;

    /// <summary>Creates the window over its shell view model.</summary>
    /// <param name="viewModel">Shell view model, set as the data context.</param>
    /// <param name="logger">Logs the shell initialization outcome.</param>
    public MainWindow(MainWindowViewModel viewModel, ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ExitRequested += OnExitRequested;
        Loaded += OnLoadedAsync;
        Activated += OnFirstActivated;
    }

    /// <summary>
    /// Plays the Foundry Deploy welcome-to-wizard transition on the page host.
    /// </summary>
    public void AnimatePageTransition()
    {
        TransitionAnimator.FadeAndTranslateY(ContentHost, ContentTransform, 12);
    }

    /// <summary>
    /// Asks the view model before closing (Alt+F4: the borderless window has no close button; the "Skip registration" button
    /// goes through the view model command). The close is cancelled and the question runs after the Closing event, because a
    /// window cannot be closed again from inside its own Closing event.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isCloseAllowed)
        {
            return;
        }

        e.Cancel = true;
        if (!_isConfirmingClose)
        {
            _isConfirmingClose = true;
            _ = Dispatcher.InvokeAsync(ConfirmCloseAsync);
        }
    }

    /// <summary>Ends the application with the exit code decided by the run.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        int exitCode = _viewModel.ExitCode;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ExitRequested -= OnExitRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
        Application.Current?.Shutdown(exitCode);
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            if (await _viewModel.ConfirmCloseAsync())
            {
                CloseNow();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("The close confirmation failed. FailureType={FailureType}", exception.GetType().Name);
        }
        finally
        {
            _isConfirmingClose = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage) && _viewModel.CurrentPage == AutopilotPage.GroupTag)
        {
            AnimatePageTransition();
        }
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        // During the close question, the question's own continuation closes the window once it returns.
        if (_isConfirmingClose)
        {
            _isCloseAllowed = true;
            return;
        }

        CloseNow();
    }

    private void CloseNow()
    {
        if (_isClosed)
        {
            return;
        }

        _isCloseAllowed = true;
        Close();
    }

    // Like Foundry Deploy's keyboard hand-off: the layout is switched once the window is active.
    private void OnFirstActivated(object? sender, EventArgs e)
    {
        Activated -= OnFirstActivated;
        _viewModel.ApplyInheritedKeyboardLayout();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            // The footer is informative; the window stays usable without it.
            _logger.LogWarning(
                "Shell initialization failed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name,
                exception.HResult);
        }
    }
}
