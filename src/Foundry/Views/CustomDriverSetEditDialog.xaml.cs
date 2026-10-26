// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Renames the selected driver set; it stays open when the name is already used in the configuration.</summary>
public sealed partial class CustomDriverSetEditDialog : ContentDialog
{
    private readonly string? setId;
    public CustomDriversViewModel ViewModel { get; }

    public CustomDriverSetEditDialog(CustomDriversViewModel viewModel)
    {
        ViewModel = viewModel;
        setId = viewModel.SelectedSet?.Reference.Id;
        InitializeComponent();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.SelectedSet?.Reference.Id != setId || !ViewModel.CanRename) return;
        ViewModel.RenameCommand.Execute(null);
        args.Cancel = ViewModel.HasStatus;
    }
}
