// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives "Change password" and "Reset publication" (spec 6.1 and 7) with a new publication password. Change rewrites the current
/// manifest only; Reset also encrypts every content file again with a new key, the complete revocation. The dialog says which.
/// </summary>
public sealed partial class ChangePublicationPasswordDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IApplicationLocalizationService localization;
    private bool disposed;

    public ChangePublicationPasswordDialogViewModel(ConfigurationSharingService sharing, IApplicationLocalizationService localization, bool isReset)
    {
        this.sharing = sharing;
        this.localization = localization;
        IsReset = isReset;
        Fields = new PublicationPasswordFields(localization, "NewPublicationPasswordLabel");
        Fields.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanApply));
        localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets whether the dialog resets the publication rather than only changing its password.</summary>
    public bool IsReset { get; }

    /// <summary>Gets the new password fields.</summary>
    public PublicationPasswordFields Fields { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanApply))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanApply => CanEdit && Fields.IsComplete;
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Gets the status of the change, or null when nothing changed.</summary>
    public SharingStatus? Result { get; private set; }

    public string Title => Text(IsReset ? "ResetPublicationTitle" : "ChangePasswordTitle");
    public string DescriptionText => Text(IsReset ? "ResetPublicationDescription" : "ChangePasswordDescription");
    public string ApplyLabel => Text(IsReset ? "ResetButton" : "ChangeButton");
    public string CancelLabel => localization.GetString("Common.Cancel");

    /// <summary>Changes the password or resets the publication; returns true when the dialog can close.</summary>
    public async Task<bool> ApplyAsync()
    {
        if (!CanApply) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            SharingStatus status = IsReset
                ? await sharing.ResetPublicationAsync(Fields.Password)
                : await sharing.ChangePasswordAsync(Fields.Password);

            // Without readable keys nothing was stored; any other outcome stored the new password and completes at the next publish.
            if (status == SharingStatus.PasswordsNeeded)
            {
                ErrorMessage = localization.GetString(SharingStatusText.Key(SharingRole.Reference, status));
                return false;
            }

            Result = status;
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "The publication password could not be changed from the dialog. Reset={Reset}", IsReset);
            ErrorMessage = Text("OperationFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
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
