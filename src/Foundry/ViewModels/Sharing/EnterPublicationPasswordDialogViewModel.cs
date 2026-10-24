// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the follower's "Enter password" (spec 6.2 and 7): the publication password entered again after it could not be read from
/// the vault or after the reference changed it. It is stored only when it opens the publication.
/// </summary>
public sealed partial class EnterPublicationPasswordDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IApplicationLocalizationService localization;
    private bool disposed;

    public EnterPublicationPasswordDialogViewModel(ConfigurationSharingService sharing, IApplicationLocalizationService localization)
    {
        this.sharing = sharing;
        this.localization = localization;
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanSubmit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanSubmit => CanEdit && Password.Length > 0;
    public bool HasError => ErrorMessage.Length > 0;

    public string Title => Text("EnterPasswordTitle");
    public string DescriptionText => Text("EnterPasswordDescription");
    public string PasswordLabel => Text("PublicationPasswordLabel");
    public string SubmitLabel => localization.GetString("Common.Save");
    public string CancelLabel => localization.GetString("Common.Cancel");

    /// <summary>Opens the publication with the password; returns true when the dialog can close.</summary>
    public async Task<bool> SubmitAsync()
    {
        if (!CanSubmit) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            SharingStatus status = await sharing.SetPublicationPasswordAsync(Password);
            if (status == SharingStatus.WrongPassword)
            {
                ErrorMessage = Text("WrongPublicationPassword");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "The publication password could not be checked from the dialog.");
            ErrorMessage = Text("OperationFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string Text(string key) => localization.GetString("Sharing." + key);

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Password = string.Empty;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
