// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.ViewModels;

namespace Foundry.Controls;

/// <summary>
/// Presents the Foundry home hero and its primary actions.
/// </summary>
public sealed partial class HomeLandingHeader : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(HomeLandingViewModel),
        typeof(HomeLandingHeader),
        new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeLandingHeader"/> class.
    /// </summary>
    public HomeLandingHeader()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Occurs when the Windows ADK action is activated.
    /// </summary>
    public event RoutedEventHandler? OpenAdkRequested;

    /// <summary>
    /// Occurs when the media configuration action is activated.
    /// </summary>
    public event RoutedEventHandler? ConfigureMediaRequested;

    /// <summary>
    /// Occurs when the review and start action is activated.
    /// </summary>
    public event RoutedEventHandler? ReviewAndStartRequested;

    /// <summary>
    /// Occurs when the documentation action is activated.
    /// </summary>
    public event RoutedEventHandler? OpenDocumentationRequested;

    /// <summary>
    /// Gets or sets the view model that supplies localized home content.
    /// </summary>
    public HomeLandingViewModel? ViewModel
    {
        get => (HomeLandingViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OpenAdkTile_Click(object sender, RoutedEventArgs e)
    {
        OpenAdkRequested?.Invoke(this, e);
    }

    private void ConfigureMediaTile_Click(object sender, RoutedEventArgs e)
    {
        ConfigureMediaRequested?.Invoke(this, e);
    }

    private void ReviewAndStartTile_Click(object sender, RoutedEventArgs e)
    {
        ReviewAndStartRequested?.Invoke(this, e);
    }

    private void OpenDocumentationTile_Click(object sender, RoutedEventArgs e)
    {
        OpenDocumentationRequested?.Invoke(this, e);
    }
}
