// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Deploy.Views;

public partial class DeploymentErrorDetailsDialog : Window
{
    public DeploymentErrorDetailsDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => DetailsTextBox.Focus();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailsTextBox.Text))
        {
            Clipboard.SetText(DetailsTextBox.Text);
        }
    }
}
