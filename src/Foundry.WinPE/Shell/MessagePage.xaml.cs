// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;

namespace Foundry.WinPE.Shell;

/// <summary>
/// A message with one or two buttons, shown in the <see cref="WinPeShellWindow"/> by the crash and obsolete-media screens. It
/// holds no decision logic: a press is recorded in its <see cref="MessagePageModel"/>.
/// </summary>
/// <remarks>It uses only the standalone application resources, so the guardian and the legacy executable can host it.</remarks>
public partial class MessagePage : UserControl
{
    private readonly MessagePageModel _model;

    /// <summary>Initializes the page with its texts.</summary>
    /// <param name="model">The texts of the page and the receiver of the operator's answer.</param>
    internal MessagePage(MessagePageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        TitleText.Text = model.Title;
        MessageText.Text = model.Message;
        PrimaryButton.Content = model.PrimaryText;
        SecondaryButton.Content = model.SecondaryText;
        SecondaryButton.Visibility = model.SecondaryText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PrimaryButton_OnClick(object sender, RoutedEventArgs e) => _model.Choose(MessageChoice.Primary);

    private void SecondaryButton_OnClick(object sender, RoutedEventArgs e) => _model.Choose(MessageChoice.Secondary);
}
