// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class PxeOutputDialog : ContentDialog
{
    // Same geometry as the media update advisory, so the dialogs that start media creation share one width.
    private const double DesiredDialogWidth = 720;
    private const double WindowMargin = 48;
    private const double DialogChromeWidth = 48;

    public PxeOutputDialogViewModel ViewModel { get; }

    public PxeOutputDialog(PxeOutputDialogViewModel viewModel, XamlRoot hostRoot)
    {
        ViewModel = viewModel;
        InitializeComponent();
        XamlRoot = hostRoot;
        ApplyContentLayout();
        hostRoot.Changed += OnHostRootChanged;
        Closed += (_, _) => hostRoot.Changed -= OnHostRootChanged;
    }

    private void OnHostRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyContentLayout();

    /// <summary>Keeps the dialog at the advisory width while leaving a margin inside small windows.</summary>
    private void ApplyContentLayout()
    {
        double dialogWidth = Math.Min(DesiredDialogWidth, Math.Max(0, XamlRoot.Size.Width - WindowMargin));
        Resources["ContentDialogMinWidth"] = dialogWidth;
        Resources["ContentDialogMaxWidth"] = dialogWidth;
        Resources["ContentDialogMaxHeight"] = Math.Max(0, XamlRoot.Size.Height - WindowMargin);
        ContentPanel.Width = Math.Max(0, dialogWidth - DialogChromeWidth);
    }
}
