// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Wpf.Dialogs;

/// <summary>
/// Shared modal About window. Its content comes entirely from an <see cref="AboutDialogViewModel"/> set as
/// <see cref="FrameworkElement.DataContext"/>, so each application supplies its own texts and version.
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }
}
