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
    private const double DesiredDialogWidth = 720;
    private const double WindowMargin = 48;
    private const double DialogChromeWidth = 48;

    /// <inheritdoc />
    public async Task<MediaUpdateAdvisoryChoice> ShowAsync(string currentVersion, string availableVersion, bool canApplyUpdate)
    {
        var hostRoot = App.MainWindow.Content.XamlRoot;
        var body = new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture,
                localizationService.GetString("StartMedia.UpdateAdvisory.Message"), currentVersion, availableVersion),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var dialog = new ContentDialog
        {
            Title = localizationService.GetString("StartMedia.UpdateAdvisory.Title"),
            Content = body,
            PrimaryButtonText = localizationService.GetString(canApplyUpdate ? "Update.Action.Apply" : "StartMedia.UpdateAdvisory.ViewUpdate"),
            SecondaryButtonText = localizationService.GetString("StartMedia.UpdateAdvisory.CreateAnyway"),
            CloseButtonText = localizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Style = ContentDialogStyleProvider.DefaultStyle,
            XamlRoot = hostRoot
        };

        ApplyContentLayout(dialog, body, hostRoot);
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyContentLayout(dialog, body, hostRoot);
        hostRoot.Changed += OnRootChanged;
        try
        {
            ContentDialogResult result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => canApplyUpdate ? MediaUpdateAdvisoryChoice.ApplyUpdate : MediaUpdateAdvisoryChoice.ViewUpdate,
                ContentDialogResult.Secondary => MediaUpdateAdvisoryChoice.CreateAnyway,
                _ => MediaUpdateAdvisoryChoice.Cancel
            };
        }
        finally
        {
            hostRoot.Changed -= OnRootChanged;
        }
    }

    private static void ApplyContentLayout(ContentDialog dialog, TextBlock body, XamlRoot hostRoot)
    {
        double dialogWidth = Math.Min(DesiredDialogWidth, Math.Max(0, hostRoot.Size.Width - WindowMargin));
        double bodyWidth = Math.Max(0, dialogWidth - DialogChromeWidth);

        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = Math.Max(0, hostRoot.Size.Height - WindowMargin);
        body.MinWidth = bodyWidth;
        body.MaxWidth = bodyWidth;
    }
}
