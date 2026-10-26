// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives "Follow a folder": folder and publication password, a preview of the publication (reference, date, version,
/// passwords, download size), then the follow itself, which replaces the configuration.
/// </summary>
public sealed partial class FollowDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private bool disposed;

    public FollowDialogViewModel(ConfigurationSharingService sharing, IFilePickerService picker, IApplicationLocalizationService localization)
    {
        this.sharing = sharing;
        this.picker = picker;
        this.localization = localization;
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanOpen), nameof(CanFollow))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(CanFollow), nameof(PreviewReference), nameof(PreviewPublished), nameof(PreviewVersion),
        nameof(PreviewPasswords), nameof(PreviewDownload))]
    public partial PublicationReadResult? Preview { get; private set; }

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanOpen => CanEdit && Folder.Trim().Length > 0 && Password.Length > 0;
    public bool CanFollow => CanEdit && Preview is not null;
    public bool HasPreview => Preview is not null;
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Gets the status of the completed follow, or null when this PC does not follow the folder.</summary>
    public SharingStatus? Result { get; private set; }

    public string PreviewReference => Preview?.Payload?.Publisher?.Name ?? string.Empty;
    public string PreviewPublished => Preview?.Payload is { CreatedUtc: var created } && created != default
        ? created.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture) : string.Empty;
    public string PreviewVersion => Preview?.Payload?.Sequence.ToString(CultureInfo.CurrentUICulture) ?? string.Empty;
    public string PreviewPasswords => Text(Preview?.Payload?.Secrets is not null ? "PasswordsIncluded" : "PasswordsNotIncluded");
    public string PreviewDownload { get; private set; } = string.Empty;

    public string Title => Text("FollowTitle");
    public string DescriptionText => Text("FollowDialogDescription");
    public string FolderLabel => Text("FolderLabel");
    public string PasswordLabel => Text("PublicationPasswordLabel");
    public string BrowseLabel => localization.GetString("Common.Browse");
    public string OpenLabel => Text("OpenButton");
    public string FollowLabel => Text("FollowButton");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string ReferenceLabel => Text("PreviewReference");
    public string PublishedLabel => Text("PreviewPublished");
    public string VersionLabel => Text("VersionLabel");
    public string PasswordsLabel => Text("PreviewPasswords");
    public string DownloadLabel => Text("PreviewDownload");
    public string ReplaceWarning => Text("FollowReplaceWarning");

    partial void OnFolderChanged(string value) => ClearPreview();

    partial void OnPasswordChanged(string value) => ClearPreview();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (!CanEdit) return;
        try
        {
            string? path = await picker.PickFolderAsync(new FolderPickerRequest(Text("FolderPickerTitle")));
            if (path is not null && CanEdit) Folder = path;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Followed folder selection failed.");
            ErrorMessage = Text("OperationFailed");
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!CanOpen) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            PublicationReadResult result = await sharing.PreviewFollowAsync(Folder.Trim(), Password);
            if (disposed) return;
            if (result is not { Status: PublicationReadStatus.Ok, Payload: not null })
            {
                ErrorMessage = Text(PreviewErrorKey(result.Status));
                return;
            }

            PreviewDownload = ByteSizeFormatter.Format(localization, (ulong)sharing.EstimateDownloadBytes(result.Payload));
            Preview = result;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "The publication could not be opened for a follow preview.");
            if (!disposed) ErrorMessage = Text("OperationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Follows the previewed publication; returns true when the dialog can close.</summary>
    public async Task<bool> FollowAsync()
    {
        if (!CanFollow) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            Result = await sharing.FollowAsync(Folder.Trim(), Password, Preview!);
            return true;
        }
        catch (Exception exception)
        {
            // Any failure stays inline: this runs from the dialog button handler, which must never throw.
            Logger.Warning(exception, "Following a folder could not be completed from the dialog.");
            ErrorMessage = Text("OperationFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string PreviewErrorKey(PublicationReadStatus status) => status switch
    {
        PublicationReadStatus.NotYetAvailable => "PublicationNotYetAvailable",
        PublicationReadStatus.NotAPublication => "NotAPublication",
        PublicationReadStatus.WrongPassword => "WrongPublicationPassword",
        PublicationReadStatus.Tampered => "StatusTampered",
        PublicationReadStatus.Unsupported => "StatusUnsupported",
        _ => "StatusFolderUnavailable"
    };

    private void ClearPreview()
    {
        Preview = null;
        PreviewDownload = string.Empty;
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
