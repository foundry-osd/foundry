// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Drives the configuration sharing card of the Settings page (spec 4.2): export and import, and the publish and follow role with
/// its status and actions. The view shows the dialogs the view model creates through <see cref="ShowDialogAsync"/>; statuses are
/// typed and mapped to resource keys by <see cref="SharingStatusText"/>.
/// </summary>
public sealed partial class ConfigurationSharingViewModel(
    ConfigurationTransferService transfer,
    ConfigurationSharingService sharing,
    IFoundryConfigurationStateService configuration,
    IFilePickerService picker,
    IDialogService dialogs,
    IApplicationLocalizationService localization) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Success;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Gets or sets the view callback that shows the dialog of a dialog view model until it closes.</summary>
    public Func<ObservableObject, Task>? ShowDialogAsync { get; set; }

    /// <summary>
    /// Gets or sets the view callback that confirms "Stop following": null when cancelled, otherwise whether the publication password
    /// is forgotten.
    /// </summary>
    public Func<Task<bool?>>? ConfirmStopFollowingAsync { get; set; }

    private SharingRole Role => sharing.Role;
    private ConfigurationSharingEngine? Engine => sharing.IsAvailable ? sharing.Engine : null;
    private SharingStatus Status => Engine?.Status ?? SharingStatus.Idle;

    public bool IsSharingAvailable => sharing.IsAvailable;
    public bool IsSharingUnavailable => !sharing.IsAvailable;
    public bool IsNone => sharing.IsAvailable && Role == SharingRole.None;
    public bool IsReference => Role == SharingRole.Reference;
    public bool IsFollower => Role == SharingRole.Follower;
    public bool HasRole => IsReference || IsFollower;

    public string RoleText => Role switch
    {
        SharingRole.Reference => Text("RoleReference"),
        SharingRole.Follower => localization.FormatString("Sharing.RoleFollower", Engine?.State.PublisherName ?? string.Empty),
        _ => string.Empty
    };

    /// <summary>
    /// Gets the role status; while the content status holds, content that failed or stalled reads as a problem, not as a download
    /// still in progress. Any other status (a wrong password, an unknown publisher, an unavailable folder) is shown as it is.
    /// </summary>
    public string RoleStatusText => localization.GetString(!IsContentStatus ? SharingStatusText.Key(Role, Status)
        : IsContentFailed ? "Sharing.StatusContentFailed"
        : IsContentStalled ? "Sharing.StatusContentStalled"
        : SharingStatusText.Key(Role, Status));

    public InfoBarSeverity RoleStatusSeverity => !IsContentStatus ? SharingStatusText.Severity(Status)
        : IsContentFailed ? InfoBarSeverity.Error
        : IsContentStalled ? InfoBarSeverity.Warning
        : SharingStatusText.Severity(Status);

    private bool IsContentStatus => Status == SharingStatus.ContentDownloading;

    public string FolderText => Engine?.State.Folder ?? string.Empty;

    public string VersionText => Engine?.State is { } state && (IsReference ? state.LastPublishedSequence : state.AppliedSequence) is > 0 and var sequence
        ? sequence.ToString(CultureInfo.CurrentUICulture) : string.Empty;

    public bool HasVersion => VersionText.Length > 0;

    public string LastSuccessText => Engine?.LastSuccessUtc is { } success ? success.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture) : string.Empty;
    public bool HasLastSuccess => LastSuccessText.Length > 0;

    /// <summary>Gets whether content is copied right now (a publish or a fetch).</summary>
    public bool IsProgressVisible => (Status == SharingStatus.Publishing || Engine?.IsContentFetching == true) && sharing.Progress is { Total: > 0 };

    /// <summary>Gets the percentage of the publish or fetch; a fetch never reads full while an item is still installed.</summary>
    public double ProgressValue => IsPublishing ? PublishProgress.Percent(sharing.Progress) : SyncProgress.Percent(sharing.Progress);

    /// <summary>Gets the step with the item it handles, for example "Installing Drivers2…".</summary>
    public string ProgressText => IsPublishing ? SharingProgressText.Publish(sharing.Progress, localization) : SharingProgressText.Sync(sharing.Progress, localization);

    /// <summary>Gets <see cref="ProgressValue"/> formatted for the current UI culture.</summary>
    public string ProgressPercentText => SharingProgressText.Percent(ProgressValue);

    private bool IsPublishing => Status == SharingStatus.Publishing;

    /// <summary>Gets whether a reset is not complete; shown only while this PC is the reference.</summary>
    public bool IsRevocationPending => IsReference && Engine?.RevocationPending == true;

    /// <summary>Gets whether the reference keys could not be read: "Take over" is the recovery (ruling R22).</summary>
    public bool IsKeyRecoveryNeeded => IsReference && Status == SharingStatus.PasswordsNeeded;

    /// <summary>Gets whether content could not be imported and no fetch runs: a problem, not an active download.</summary>
    public bool IsContentFailed => IsFollower && Engine is { FailedContent.Count: > 0, IsContentFetching: false };

    public bool IsContentStalled => IsFollower && Engine?.IsContentStalled == true;

    public string ContentFailedText => localization.FormatString("Sharing.ContentFailed", Engine?.FailedContent.Count ?? 0);

    public bool CanTakeOver => IsReference && Status is SharingStatus.UnknownPublisher or SharingStatus.PasswordsNeeded;
    public bool CanEnterPassword => IsFollower && Status is SharingStatus.PasswordsNeeded or SharingStatus.WrongPassword;
    public bool CanAcceptReference => IsFollower && Status == SharingStatus.UnknownPublisher && Engine?.Candidate is not null;

    /// <summary>Gets a localized string of the card.</summary>
    public string Text(string key) => localization.GetString("Sharing." + key);

    /// <summary>Maps the outcome of opening a configuration to its message resource key.</summary>
    public static string StatusKey(ImportPreviewStatus status) => status switch
    {
        ImportPreviewStatus.WrongPassphrase => "Sharing.WrongPassphrase",
        ImportPreviewStatus.Unsupported => "Sharing.UnsupportedFile",
        ImportPreviewStatus.Locked => "Sharing.LockedProfile",
        ImportPreviewStatus.Unreadable => "Sharing.UnreadableFile",
        _ => "Sharing.InvalidFile"
    };

    /// <summary>Starts following the sharing state; call when the card is shown.</summary>
    public void Attach()
    {
        sharing.Changed += OnSharingChanged;
        configuration.ManagedChanged += OnSharingChanged;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Stops following the sharing state; call when the card is hidden.</summary>
    public void Detach()
    {
        sharing.Changed -= OnSharingChanged;
        configuration.ManagedChanged -= OnSharingChanged;
    }

    /// <summary>Refreshes every text after a language change.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private async Task ExportAsync()
    {
        StatusMessage = string.Empty;
        using var dialog = new ExportConfigurationDialogViewModel(transfer, picker, localization);
        if (!await ShowAsync(dialog)) return;
        if (dialog.IsCompleted) ShowStatus("ExportCompleted", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        StatusMessage = string.Empty;
        if (configuration.IsManaged)
        {
            ShowStatus("ImportRefusedFollowing", InfoBarSeverity.Warning);
            return;
        }

        using var dialog = new ImportConfigurationDialogViewModel(transfer, picker, localization);
        if (!await ShowAsync(dialog)) return;
        if (dialog.Result is not { } result) return;
        if (!result.PasswordsSaved) ShowStatus("ImportPasswordsNotSaved", InfoBarSeverity.Warning);
        else if (result.MissingContent.Count > 0) ShowStatus("ImportCompletedMissingContent", InfoBarSeverity.Warning);
        else ShowStatus("ImportCompleted", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        StatusMessage = string.Empty;
        using var dialog = new PublishDialogViewModel(sharing, picker, localization);
        if (!await ShowAsync(dialog)) return;
        if (dialog.NeedsTakeOver) await ShowTakeOverAsync(dialog.Folder.Trim(), dialog.IncludePasswords, isRecovery: false);
    }

    [RelayCommand]
    private async Task FollowAsync()
    {
        StatusMessage = string.Empty;
        using var dialog = new FollowDialogViewModel(sharing, picker, localization);
        await ShowAsync(dialog);
    }

    [RelayCommand]
    private async Task TakeOverAsync()
    {
        if (Engine?.State is not { Folder: { } folder } state) return;
        StatusMessage = string.Empty;
        await ShowTakeOverAsync(folder, state.IncludePasswords, isRecovery: Status == SharingStatus.PasswordsNeeded);
    }

    [RelayCommand]
    private Task RetryAsync() => RunAsync(sharing.RetryPublishAsync);

    [RelayCommand]
    private Task CheckNowAsync() => RunAsync(sharing.CheckNowAsync);

    [RelayCommand]
    private Task ChangePasswordAsync() => ShowPasswordChangeAsync(isReset: false);

    [RelayCommand]
    private Task ResetPublicationAsync() => ShowPasswordChangeAsync(isReset: true);

    [RelayCommand]
    private async Task StopPublishingAsync()
    {
        StatusMessage = string.Empty;
        if (!await dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                Text("StopPublishingTitle"), Text("StopPublishingMessage"), Text("StopPublishingButton"), localization.GetString("Common.Cancel"),
                PreferCancel: true))) return;
        await RunAsync(async () =>
        {
            await sharing.StopPublishingAsync();
            return SharingStatus.Idle;
        });
    }

    [RelayCommand]
    private async Task EnterPasswordAsync()
    {
        StatusMessage = string.Empty;
        using var dialog = new EnterPublicationPasswordDialogViewModel(sharing, localization);
        await ShowAsync(dialog);
    }

    [RelayCommand]
    private async Task AcceptReferenceAsync()
    {
        if (Engine?.Candidate is not { } candidate) return;
        StatusMessage = string.Empty;
        using var dialog = new AcceptPublisherDialogViewModel(sharing, localization, candidate, Engine.PinnedKeyId);
        await ShowAsync(dialog);
    }

    [RelayCommand]
    private async Task StopFollowingAsync()
    {
        if (ConfirmStopFollowingAsync is null) return;
        StatusMessage = string.Empty;
        if (await ConfirmStopFollowingAsync() is not { } forgetPassword) return;
        await RunAsync(async () =>
        {
            await sharing.StopFollowingAsync(forgetPassword);
            return SharingStatus.Idle;
        });
    }

    private async Task ShowTakeOverAsync(string folder, bool includePasswords, bool isRecovery)
    {
        using var dialog = new TakeOverDialogViewModel(sharing, localization, folder, includePasswords, Engine?.ForeignPublication, isRecovery);
        await ShowAsync(dialog);
    }

    private async Task ShowPasswordChangeAsync(bool isReset)
    {
        StatusMessage = string.Empty;
        using var dialog = new ChangePublicationPasswordDialogViewModel(sharing, localization, isReset);
        if (!await ShowAsync(dialog) || dialog.Result is not { } result) return;
        if (result != SharingStatus.Published) ShowStatus("PasswordChangePending", InfoBarSeverity.Warning);
        else ShowStatus(isReset ? "PublicationReset" : "PasswordChanged", InfoBarSeverity.Success);
    }

    private async Task<bool> ShowAsync(ObservableObject dialog)
    {
        if (ShowDialogAsync is null) return false;
        await ShowDialogAsync(dialog);
        return true;
    }

    /// <summary>Runs a card action; a failure is logged and shown on the card, never thrown to the command.</summary>
    private async Task RunAsync(Func<Task<SharingStatus>> action)
    {
        StatusMessage = string.Empty;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "A configuration sharing action failed.");
            ShowStatus("OperationFailed", InfoBarSeverity.Error);
        }

        OnPropertyChanged(string.Empty);
    }

    private void ShowStatus(string key, InfoBarSeverity severity)
    {
        StatusSeverity = severity;
        StatusMessage = Text(key);
    }

    private void OnSharingChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
