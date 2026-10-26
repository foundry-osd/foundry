// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Confirms "Stop following"; Cancel is the default button, and the check box chooses to forget the publication password.</summary>
public sealed partial class StopFollowingDialog : ContentDialog
{
    public StopFollowingDialog(ConfigurationSharingViewModel viewModel, string cancelLabel)
    {
        ViewModel = viewModel;
        CancelLabel = cancelLabel;
        InitializeComponent();
    }

    public ConfigurationSharingViewModel ViewModel { get; }

    public string CancelLabel { get; }

    /// <summary>Gets whether the publication password is forgotten with the role.</summary>
    public bool ForgetPassword => ForgetPasswordCheckBox.IsChecked == true;
}
