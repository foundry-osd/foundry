// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the export dialog: passphrase with its rule, generation and confirmation, the "Include passwords" choice, the
/// destination picker and the export itself. Failures stay inline; the passphrase is cleared when the dialog closes.
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
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanCopy), nameof(ValidationMessage), nameof(HasValidationMessage))]
    public partial string Passphrase { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(ValidationMessage), nameof(HasValidationMessage))]
    public partial string Confirmation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludePasswords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport), nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPassphraseRevealed { get; set; }

    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !disposed;
    public bool IsPassphraseValid => PassphrasePolicy.Validate(Passphrase) == PassphraseValidation.Valid;
    public bool CanCopy => CanEdit && IsPassphraseValid;
    public bool CanExport => CanEdit && IsPassphraseValid && string.Equals(Passphrase, Confirmation, StringComparison.Ordinal);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>Gets the rule the passphrase still breaks, or the mismatch once a confirmation is typed; empty while typing starts.</summary>
    public string ValidationMessage =>
        Passphrase.Length > 0 && !IsPassphraseValid ? Text("PassphraseRule")
        : Confirmation.Length > 0 && !string.Equals(Passphrase, Confirmation, StringComparison.Ordinal) ? Text("PassphraseMismatch")
        : string.Empty;

    public string Title => Text("ExportTitle");
    public string ExportLabel => Text("ExportButton");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string PassphraseLabel => Text("PassphraseLabel");
    public string ConfirmationLabel => Text("ConfirmPassphraseLabel");
    public string RuleText => Text("PassphraseRule");
    public string GenerateLabel => Text("GeneratePassphrase");
    public string CopyLabel => Text("CopyPassphrase");
    public string CopiedText => Text("PassphraseCopied");
    public string IncludePasswordsLabel => Text("IncludePasswords");
    public string IncludePasswordsWarning => Text("IncludePasswordsWarning");
    public string DescriptionText => Text("ExportDescription");

    /// <summary>Gets whether the export completed.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Fills both passphrase fields with a generated passphrase and shows it so it can be noted or copied.</summary>
    [RelayCommand]
    private void Generate()
    {
        if (!CanEdit) return;
        string generated = PassphrasePolicy.Generate();
        Passphrase = generated;
        Confirmation = generated;
        IsPassphraseRevealed = true;
    }

    partial void OnPassphraseChanged(string value) => IsCopied = false;

    /// <summary>Asks for the destination, then writes the export; returns true when the file was written.</summary>
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
            await transfer.ExportAsync(path, Passphrase, IncludePasswords);
            IsCompleted = true;
            return true;
        }
        catch (InvalidDataException)
        {
            ErrorMessage = Text("ExportFilesTooLarge");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            ErrorMessage = Text("ExportFailed");
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    /// <summary>Records that the passphrase was copied; the view owns the clipboard.</summary>
    public void MarkCopied() => IsCopied = true;

    private string Text(string key) => localization.GetString("Sharing." + key);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Passphrase = string.Empty;
        Confirmation = string.Empty;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
