// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives "Take over": the current publication password continues a folder with this PC's key. The foreign version and
/// key id are shown as found in the folder, explicitly not verified: neither is authenticated until the password opens the manifest.
/// Also the recovery of a reference whose keys could not be read.
/// </summary>
public sealed partial class TakeOverDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IApplicationLocalizationService localization;
    private readonly SharingForeignPublication? foreign;
    private readonly bool isRecovery;
    private bool disposed;

    /// <param name="sharing">Sharing host.</param>
    /// <param name="localization">Localization.</param>
    /// <param name="folder">The folder to take over.</param>
    /// <param name="includePasswords">Initial "Include passwords" choice.</param>
    /// <param name="foreign">The foreign publication the last publish found, or null when none was read.</param>
    /// <param name="isRecovery">Whether this is the recovery after the publication keys could not be read.</param>
    public TakeOverDialogViewModel(ConfigurationSharingService sharing, IApplicationLocalizationService localization, string folder, bool includePasswords,
        SharingForeignPublication? foreign, bool isRecovery)
    {
        this.sharing = sharing;
        this.localization = localization;
        this.foreign = foreign;
        this.isRecovery = isRecovery;
        Folder = folder;
        IncludePasswords = includePasswords;
        Progress = new PublishProgressIndicator(sharing, localization);
        localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets the folder to take over.</summary>
    public string Folder { get; }

    /// <summary>Gets the progress of the take over publish while it runs.</summary>
    public PublishProgressIndicator Progress { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTakeOver))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludePasswords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanTakeOver))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanTakeOver => CanEdit && PassphrasePolicy.Validate(Password) == PassphraseValidation.Valid;
    public bool HasError => ErrorMessage.Length > 0;
    public bool HasForeign => foreign is not null;

    /// <summary>Gets whether the take over published.</summary>
    public bool IsCompleted { get; private set; }

    public string ForeignVersion => foreign?.Sequence.ToString(CultureInfo.CurrentUICulture) ?? string.Empty;
    public string ForeignKey => foreign?.KeyId ?? string.Empty;

    public string Title => Text("TakeOverTitle");
    public string DescriptionText => Text(isRecovery ? "TakeOverRecoveryDescription" : "TakeOverDescription");
    public string FolderLabel => Text("FolderLabel");
    public string ForeignVersionLabel => Text("ForeignVersionLabel");
    public string ForeignKeyLabel => Text("ForeignKeyLabel");
    public string NotVerifiedText => Text("NotVerified");
    public string PasswordLabel => Text("CurrentPublicationPasswordLabel");
    public string IncludePasswordsLabel => Text("IncludePasswords");
    public string IncludePasswordsWarning => Text("IncludePasswordsPublicationWarning");
    public string TakeOverLabel => Text("TakeOverButton");
    public string CancelLabel => localization.GetString("Common.Cancel");

    /// <summary>Takes the folder over; returns true when the dialog can close.</summary>
    public async Task<bool> TakeOverAsync()
    {
        if (!CanTakeOver) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        Progress.Start();
        try
        {
            SharingStatus status = await sharing.TakeOverAsync(Folder, Password, IncludePasswords);
            if (status == SharingStatus.Published)
            {
                IsCompleted = true;
                return true;
            }

            // A forged manifest also reads as a wrong password; either way this password does not open the current publication.
            ErrorMessage = status == SharingStatus.WrongPassword
                ? Text("TakeOverWrongPassword")
                : localization.GetString(SharingStatusText.Key(SharingRole.Reference, status));
            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Publication take over could not be completed from the dialog.");
            ErrorMessage = Text("OperationFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
            Progress.Stop();
        }
    }

    private string Text(string key) => localization.GetString("Sharing." + key);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Password = string.Empty;
        Progress.Dispose();
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
