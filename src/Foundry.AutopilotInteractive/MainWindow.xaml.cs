// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Wpf.Motion;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive;

/// <summary>
/// Full-screen shell window: menu, version, page host and device footer.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;

    /// <param name="viewModel">Shell view model, set as the data context.</param>
    /// <param name="logger">Logs the shell initialization outcome.</param>
    public MainWindow(MainWindowViewModel viewModel, ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
    }

    /// <summary>
    /// Plays the Deploy page transition on the page host. Call it after the displayed page changed.
    /// </summary>
    public void AnimatePageTransition()
    {
        TransitionAnimator.FadeAndTranslateY(ContentHost, ContentTransform, 12);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
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
