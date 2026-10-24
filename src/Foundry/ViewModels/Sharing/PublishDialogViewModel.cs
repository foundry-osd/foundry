// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives "Publish to a folder" (spec 6.1): folder, publication password with its rule, "Include passwords", then the first
/// publish. A folder another PC publishes to closes the dialog with <see cref="NeedsTakeOver"/> so the card offers "Take over".
/// </summary>
public sealed partial class PublishDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private bool disposed;

    public PublishDialogViewModel(ConfigurationSharingService sharing, IFilePickerService picker, IApplicationLocalizationService localization)
    {
        this.sharing = sharing;
        this.picker = picker;
        this.localization = localization;
        Fields = new PublicationPasswordFields(localization, "PublicationPasswordLabel");
        Fields.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanPublish));
        localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Gets the publication password fields.</summary>
    public PublicationPasswordFields Fields { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludePasswords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanPublish))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanEdit => !IsBusy && !disposed;
    public bool CanPublish => CanEdit && Folder.Trim().Length > 0 && Fields.IsComplete;
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Gets the status of the completed publish, or null when nothing was published.</summary>
    public SharingStatus? Result { get; private set; }

    /// <summary>Gets whether another PC publishes to the folder: the card continues with "Take over".</summary>
    public bool NeedsTakeOver { get; private set; }

    public string Title => Text("PublishTitle");
    public string DescriptionText => Text("PublishDialogDescription");
    public string FolderLabel => Text("FolderLabel");
    public string BrowseLabel => localization.GetString("Common.Browse");
    public string PublishLabel => Text("PublishButton");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string IncludePasswordsLabel => Text("IncludePasswords");
    public string IncludePasswordsWarning => Text("IncludePasswordsPublicationWarning");

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
            Logger.Warning(exception, "Publication folder selection failed.");
            ErrorMessage = Text("OperationFailed");
        }
    }

    /// <summary>Publishes to the folder; returns true when the dialog can close.</summary>
    public async Task<bool> PublishAsync()
    {
        if (!CanPublish) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            SharingStatus status = await sharing.StartPublishingAsync(Folder.Trim(), Fields.Password, IncludePasswords);
            switch (status)
            {
                case SharingStatus.Published or SharingStatus.NotPublishedIncomplete:
                    Result = status;
                    return true;
                case SharingStatus.UnknownPublisher:
                    NeedsTakeOver = true;
                    return true;
                default:
                    ErrorMessage = localization.GetString(SharingStatusText.Key(SharingRole.Reference, status));
                    return false;
            }
        }
        catch (Exception exception)
        {
            // Any failure stays inline: this runs from the dialog button handler, which must never throw.
            Logger.Warning(exception, "Publishing to a folder could not be started from the dialog.");
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
