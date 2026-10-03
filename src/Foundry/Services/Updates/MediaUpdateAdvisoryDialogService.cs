// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Services.Application;
using Foundry.Services.Localization;

namespace Foundry.Services.Updates;

/// <summary>
/// Displays the native three-button advisory without changing an open dialog's update action.
/// </summary>
internal sealed class MediaUpdateAdvisoryDialogService(IApplicationLocalizationService localizationService) : IMediaUpdateAdvisoryDialogService
{
    /// <inheritdoc />
    public async Task<MediaUpdateAdvisoryChoice> ShowAsync(string currentVersion, string availableVersion, bool canApplyUpdate)
    {
        var dialog = new ContentDialog
        {
            Title = localizationService.GetString("StartMedia.UpdateAdvisory.Title"),
            Content = new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture,
                    localizationService.GetString("StartMedia.UpdateAdvisory.Message"), currentVersion, availableVersion),
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 360,
                MaxWidth = 520,
                Margin = new Thickness(0, 4, 0, 8)
            },
            PrimaryButtonText = localizationService.GetString(canApplyUpdate ? "Update.Action.Apply" : "StartMedia.UpdateAdvisory.ViewUpdate"),
            SecondaryButtonText = localizationService.GetString("StartMedia.UpdateAdvisory.CreateAnyway"),
            CloseButtonText = localizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Style = ContentDialogStyleProvider.DefaultStyle,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => canApplyUpdate ? MediaUpdateAdvisoryChoice.ApplyUpdate : MediaUpdateAdvisoryChoice.ViewUpdate,
            ContentDialogResult.Secondary => MediaUpdateAdvisoryChoice.CreateAnyway,
            _ => MediaUpdateAdvisoryChoice.Cancel
        };
    }
}
