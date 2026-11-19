// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using Foundry.Connect.ViewModels;
using Foundry.Wpf.Shell;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect;

/// <summary>
/// The Foundry Connect page of the WinPE shell window: the menu row, the boot media banner, the version and the network
/// page, shown by the phase when the network gate does not skip Connect. The status page shown before it
/// reports the phase's readiness; <see cref="Detach"/> releases it from the shell when the phase ends.
/// </summary>
public partial class ConnectShellContent : UserControl
{
    private readonly ILogger<ConnectShellContent> _logger;
    private readonly ShellContentAttachment _shellAttachment;

    /// <summary>Initializes the page and binds it to its view model.</summary>
    /// <param name="viewModel">The Connect page view model.</param>
    /// <param name="logger">The page logger.</param>
    /// <param name="shell">The shell window that shows this page.</param>
    public ConnectShellContent(
        MainWindowViewModel viewModel,
        ILogger<ConnectShellContent> logger,
        IShellHost shell)
    {
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        _shellAttachment = new ShellContentAttachment(shell, this);
    }

    /// <summary>
    /// Releases the page from the shell: removes its handlers from the shell, which may
    /// keep showing the page (disabled) after the phase. Called when the phase ends; safe to call more than once.
    /// </summary>
    internal void Detach() => _shellAttachment.Dispose();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _logger.LogInformation("Connect content loaded.");
    }
}
