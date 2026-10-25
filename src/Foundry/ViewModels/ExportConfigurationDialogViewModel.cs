// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the export dialog: the passphrase fields, the "Include passwords" choice, the destination picker and the export itself.
/// Failures stay inline; the passphrase is cleared when the dialog closes.
/// </summary>
public sealed partial class ExportConfigurationDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationTransferService transfer;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private bool disposed;

    public ExportConfigurationDialogViewModel(ConfigurationTransferService transfer, IFilePickerService picker, IApplicationLocalizationService localization)
    {
        this.transfer = transfer;
        this.picker = picker;
        this.localization = localization;
        Fields = new NewSecretFields(localization, NewSecretTexts.ExportPassphrase);
        Fields.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanExport));
        localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets the passphrase fields.</summary>
    public NewSecretFields Fields { get; }

    [ObservableProperty]
    public partial bool IncludePasswords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !IsCompleted && !disposed;
    public bool CanExport => CanEdit && Fields.IsComplete;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasWarning => !string.IsNullOrEmpty(WarningMessage);

    public string Title => Text("ExportTitle");
    public string ExportLabel => Text("ExportButton");
    public string CancelLabel => localization.GetString(IsCompleted ? "Common.Close" : "Common.Cancel");
    public string IncludePasswordsLabel => Text("IncludePasswords");
    public string IncludePasswordsWarning => Text("IncludePasswordsWarning");
    public string DescriptionText => Text("ExportDescription");

    /// <summary>Gets whether the export file was written; the form is then read-only and only Close remains.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanExport), nameof(CancelLabel))]
    public partial bool IsCompleted { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string WarningMessage { get; set; } = string.Empty;

    /// <summary>
    /// Asks for the destination, then writes the export. Returns true when the file was written with every referenced file; when
    /// some files were missing it returns false with <see cref="IsCompleted"/> set and a warning, so the dialog stays open.
    /// </summary>
    public async Task<bool> ExportAsync()
    {
        if (!CanExport) return false;
        ErrorMessage = string.Empty;
        string? path;
        try
        {
            path = await picker.PickSaveFileAsync(new FileSavePickerRequest(
                Title,
                "Foundry-configuration",
                new FilePickerTypeChoice[] { new(Text("FileTypeName"), new string[] { ConfigurationTransfer.FileExtension }) },
                ConfigurationTransfer.FileExtension));
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Configuration export destination selection failed.");
            ErrorMessage = Text("ExportFailed");
            return false;
        }

        if (path is null || !CanExport) return false;
        IsBusy = true;
        try
        {
            int skippedFileCount = await transfer.ExportAsync(path, Fields.Password, IncludePasswords);
            IsCompleted = true;
            if (skippedFileCount == 0) return true;
            // Keep the dialog open so the user reads which part of the configuration the file lacks.
            WarningMessage = localization.FormatString("Sharing.ExportSkippedFiles", skippedFileCount);
            return false;
        }
        catch (InvalidDataException)
        {
            ErrorMessage = Text("ExportFilesTooLarge");
        }
        catch (Exception)
        {
            // The transfer service has logged the failure; nothing escapes to the dialog event handler.
            ErrorMessage = Text("ExportFailed");
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    private string Text(string key) => localization.GetString("Sharing." + key);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        Fields.RefreshTexts();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Fields.Clear();
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
