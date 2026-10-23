// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the PXE output dialog: the destination folder, the warning about files it already holds, and the list of
/// imported content that a PXE start does not receive.
/// </summary>
public sealed partial class PxeOutputDialogViewModel : ObservableObject
{
    private static readonly TimeSpan ExistingImageProbeDelay = TimeSpan.FromMilliseconds(400);
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;

    public PxeOutputDialogViewModel(IFilePickerService picker, IApplicationLocalizationService localization,
        string initialFolderPath, PxeMissingContent missingContent)
    {
        this.picker = picker;
        this.localization = localization;
        MissingContentMessage = BuildMissingContentMessage(missingContent);
        FolderPath = initialFolderPath;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasExistingImage { get; set; }

    public bool CanCreate => Path.IsPathFullyQualified(FolderPath.Trim());
    public bool HasMissingContent => MissingContentMessage.Length > 0;
    public string MissingContentMessage { get; }
    public string Title => localization.GetString("StartMedia.CreatePxeButton");
    public string Description => localization.GetString("StartMedia.PxeDialog.Description");
    public string FolderLabel => localization.GetString("StartMedia.PxeDialog.FolderLabel");
    public string BrowseLabel => localization.GetString("Common.Browse");
    public string CreateLabel => localization.GetString("StartMedia.PxeDialog.CreateButton");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string ExistingImageWarning => localization.GetString("StartMedia.PxeDialog.ExistingImageWarning");
    public string MissingContentTitle => localization.GetString("StartMedia.PxeDialog.MissingContentTitle");

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? path = await picker.PickFolderAsync(new FolderPickerRequest(localization.GetString("StartMedia.PxeDialog.FolderPickerTitle")));
        if (!string.IsNullOrWhiteSpace(path))
        {
            FolderPath = path;
        }
    }

    partial void OnFolderPathChanged(string value)
    {
        _ = RefreshExistingImageAsync(value);
    }

    /// <summary>
    /// Checks the folder off the UI thread, because a network path can take seconds to answer. Typing waits for a short pause
    /// so partial paths are not probed, and only the answer for the path still displayed is kept.
    /// </summary>
    private async Task RefreshExistingImageAsync(string folderPath)
    {
        await Task.Delay(ExistingImageProbeDelay);
        if (!string.Equals(FolderPath, folderPath, StringComparison.Ordinal))
        {
            return;
        }

        bool containsBootImage = await Task.Run(() => ContainsBootImage(folderPath));
        if (string.Equals(FolderPath, folderPath, StringComparison.Ordinal))
        {
            HasExistingImage = containsBootImage;
        }
    }

    private static bool ContainsBootImage(string folderPath)
    {
        try
        {
            return Path.IsPathFullyQualified(folderPath.Trim()) && File.Exists(Path.Combine(folderPath.Trim(), "boot.wim"));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string BuildMissingContentMessage(PxeMissingContent missing)
    {
        if (!missing.HasAny)
        {
            return string.Empty;
        }

        List<string> lines = [];
        AddLine("StartMedia.PxeDialog.MissingCustomImagesFormat", missing.CustomImageCount);
        AddLine("StartMedia.PxeDialog.MissingCustomDriversFormat", missing.CustomDriverSetCount);
        AddLine("StartMedia.PxeDialog.MissingPostInstallationFormat", missing.PostInstallationActionCount);
        lines.Add(string.Empty);
        lines.Add(localization.GetString("StartMedia.PxeDialog.MissingContentMessage"));
        return string.Join(Environment.NewLine, lines);

        void AddLine(string resourceKey, int count)
        {
            if (count > 0)
            {
                lines.Add($"• {localization.FormatString(resourceKey, count)}");
            }
        }
    }
}
