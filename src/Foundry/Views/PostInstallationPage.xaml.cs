// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Views;

public sealed partial class PostInstallationPage : Page
{
    private bool editorOpen;
    public PostInstallationViewModel ViewModel { get; }

    public PostInstallationPage()
    {
        ViewModel = App.GetService<PostInstallationViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private async void OnAddClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuFlyoutItem item && Enum.TryParse(item.Tag?.ToString(), out PreOobeActionKind kind))
            await ShowEditorAsync(kind);
    }

    private async void OnEditClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.CanEdit) await ShowEditorAsync(null);
    }

    private async Task ShowEditorAsync(PreOobeActionKind? kind)
    {
        if (editorOpen) return;
        editorOpen = true;
        try
        {
            using var editor = ViewModel.CreateEditor(kind);
            var dialog = new PostInstallationActionDialog(editor, XamlRoot);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.SaveEditor(editor);
        }
        finally { editorOpen = false; }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
