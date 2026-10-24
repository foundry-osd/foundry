// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the configuration sharing card of the Settings page: the export and import commands and the outcome message. The
/// view shows the dialogs through <see cref="ShowExportDialogAsync"/> and <see cref="ShowImportDialogAsync"/>.
/// </summary>
public sealed partial class ConfigurationSharingViewModel(
    ConfigurationTransferService transfer,
    IFilePickerService picker,
    IApplicationLocalizationService localization) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Success;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Gets or sets the view callback that shows the export dialog until it closes.</summary>
    public Func<ExportConfigurationDialogViewModel, Task>? ShowExportDialogAsync { get; set; }

    /// <summary>Gets or sets the view callback that shows the import dialog until it closes.</summary>
    public Func<ImportConfigurationDialogViewModel, Task>? ShowImportDialogAsync { get; set; }

    /// <summary>Gets a localized string of the card.</summary>
    public string Text(string key) => localization.GetString(key);

    /// <summary>Maps the outcome of opening a configuration to its message resource key.</summary>
    public static string StatusKey(ImportPreviewStatus status) => status switch
    {
        ImportPreviewStatus.WrongPassphrase => "Sharing.WrongPassphrase",
        ImportPreviewStatus.Unsupported => "Sharing.UnsupportedFile",
        ImportPreviewStatus.Locked => "Sharing.LockedProfile",
        ImportPreviewStatus.Unreadable => "Sharing.UnreadableFile",
        _ => "Sharing.InvalidFile"
    };

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (ShowExportDialogAsync is null) return;
        StatusMessage = string.Empty;
        using var dialog = new ExportConfigurationDialogViewModel(transfer, picker, localization);
        await ShowExportDialogAsync(dialog);
        if (dialog.IsCompleted) ShowStatus("Sharing.ExportCompleted", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (ShowImportDialogAsync is null) return;
        StatusMessage = string.Empty;
        using var dialog = new ImportConfigurationDialogViewModel(transfer, picker, localization);
        await ShowImportDialogAsync(dialog);
        if (dialog.Result is not { } result) return;
        if (!result.PasswordsSaved) ShowStatus("Sharing.ImportPasswordsNotSaved", InfoBarSeverity.Warning);
        else if (result.MissingContent.Count > 0) ShowStatus("Sharing.ImportCompletedMissingContent", InfoBarSeverity.Warning);
        else ShowStatus("Sharing.ImportCompleted", InfoBarSeverity.Success);
    }

    private void ShowStatus(string key, InfoBarSeverity severity)
    {
        StatusSeverity = severity;
        StatusMessage = Text(key);
    }
}
