// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives "Accept new reference" (spec 6.2): shows the pinned key id and the new one, which the current manifest authenticated with
/// the publication password, then pins the new key.
/// </summary>
public sealed partial class AcceptPublisherDialogViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSharingService sharing;
    private readonly IApplicationLocalizationService localization;
    private readonly SharingPublisherCandidate candidate;
    private readonly string? previousKeyId;
    private bool disposed;

    public AcceptPublisherDialogViewModel(ConfigurationSharingService sharing, IApplicationLocalizationService localization, SharingPublisherCandidate candidate,
        string? previousKeyId)
    {
        this.sharing = sharing;
        this.localization = localization;
        this.candidate = candidate;
        this.previousKeyId = previousKeyId;
        localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool CanAccept => !IsBusy && !disposed;
    public bool HasError => ErrorMessage.Length > 0;

    public string PreviousKey => previousKeyId ?? string.Empty;
    public string NewKey => candidate.KeyId;
    public string NewVersion => candidate.Sequence.ToString(CultureInfo.CurrentUICulture);

    public string Title => Text("AcceptReferenceTitle");
    public string DescriptionText => localization.FormatString("Sharing.AcceptReferenceDescription", candidate.Name);
    public string PreviousKeyLabel => Text("PreviousKeyLabel");
    public string NewKeyLabel => Text("NewKeyLabel");
    public string VersionLabel => Text("VersionLabel");
    public string AcceptLabel => Text("AcceptButton");
    public string CancelLabel => localization.GetString("Common.Cancel");

    /// <summary>Accepts the new reference; returns true when the dialog can close.</summary>
    public async Task<bool> AcceptAsync()
    {
        if (!CanAccept) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            SharingStatus status = await sharing.AcceptNewPublisherAsync();
            if (status is SharingStatus.Updated or SharingStatus.UpToDate or SharingStatus.ContentDownloading or SharingStatus.PasswordsNeeded) return true;
            ErrorMessage = localization.GetString(SharingStatusText.Key(SharingRole.Follower, status));
            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "The new reference could not be accepted from the dialog.");
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
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
