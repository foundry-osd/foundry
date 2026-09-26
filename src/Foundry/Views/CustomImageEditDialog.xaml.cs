// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class CustomImageEditDialog : ContentDialog
{
    private readonly string? imageId;
    public CustomImagesViewModel ViewModel { get; }

    public CustomImageEditDialog(CustomImagesViewModel viewModel)
    {
        ViewModel = viewModel;
        imageId = viewModel.SelectedImage?.Reference.Id;
        InitializeComponent();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.SelectedImage?.Reference.Id != imageId || !ViewModel.CanRename) return;
        ViewModel.RenameCommand.Execute(null);
        args.Cancel = ViewModel.HasStatus;
    }
}
