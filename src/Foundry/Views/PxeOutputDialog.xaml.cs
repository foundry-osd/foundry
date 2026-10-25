// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class PxeOutputDialog : ContentDialog
{
    public PxeOutputDialogViewModel ViewModel { get; }

    public PxeOutputDialog(PxeOutputDialogViewModel viewModel, XamlRoot hostRoot)
    {
        ViewModel = viewModel;
        InitializeComponent();
        XamlRoot = hostRoot;
    }
}
