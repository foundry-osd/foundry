// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.NetworkCache;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.NetworkCache;
using Foundry.Services.Shell;
using Foundry.Utilities.Networking;
using Microsoft.UI.Dispatching;

namespace Foundry.ViewModels;

/// <summary>
/// Edits the network cache section: the accounts that open shares, the SMB shares, and the IP scopes that choose the
/// shares a device tries. Account passwords stay in the session secret service, keyed by account id.
/// </summary>
public sealed partial class NetworkCacheConfigurationViewModel : ObservableObject, IDisposable
{
    private const string ShareSeparator = " \u2192 ";
    private readonly IFoundryConfigurationStateService configuration;
    private readonly ManagedConfigurationViewModel managed;
    private readonly INetworkCacheSecretStateService secrets;
    private readonly IDeploymentProtectionSecretStateService protectionSecrets;
    private readonly INetworkCacheDialogService networkCacheDialogs;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly INetworkCachePublicationStateService publicationState;
    private readonly INetworkCachePublicationService publication;
    private readonly IShellNavigationGuardService navigationGuard;
    private readonly DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly NetworkCacheShareAccess shareAccess;
    private CancellationTokenSource? shareOperation;
    private IReadOnlyList<NetworkCacheValidationIssue> issues = [];
    private bool applying;
    private bool disposed;

    public NetworkCacheConfigurationViewModel(IFoundryConfigurationStateService configuration, INetworkCacheSecretStateService secrets,
        IDeploymentProtectionSecretStateService protectionSecrets, INetworkCacheDialogService networkCacheDialogs, IDialogService dialogs,
        IApplicationLocalizationService localization, ManagedConfigurationViewModel managed, ISmbConnector smbConnector,
        INetworkCachePublicationStateService publicationState, INetworkCachePublicationService publication, IShellNavigationGuardService navigationGuard)
    {
        this.configuration = configuration;
        this.managed = managed;
        this.secrets = secrets;
        this.protectionSecrets = protectionSecrets;
        this.networkCacheDialogs = networkCacheDialogs;
        this.dialogs = dialogs;
        this.localization = localization;
        this.publicationState = publicationState;
        this.publication = publication;
        this.navigationGuard = navigationGuard;
        shareAccess = new NetworkCacheShareAccess(smbConnector);
        configuration.StateChanged += OnStateChanged;
        managed.PropertyChanged += OnManagedChanged;
        secrets.Changed += OnSecretsChanged;
        protectionSecrets.Changed += OnSecretsChanged;
        localization.LanguageChanged += OnLanguageChanged;
        publicationState.Changed += OnPublicationChanged;
        navigationGuard.StateChanged += OnNavigationGuardChanged;
        ApplyState();
    }

    /// <summary>
    /// Raised before listed rows are removed. A table must drop its selection first: removing a selected row makes it
    /// scroll to a row that no longer exists.
    /// </summary>
    public event EventHandler? AccountRowsRemoving;

    /// <inheritdoc cref="AccountRowsRemoving"/>
    public event EventHandler? ShareRowsRemoving;

    /// <inheritdoc cref="AccountRowsRemoving"/>
    public event EventHandler? ScopeRowsRemoving;

    public ObservableCollection<NetworkCacheAccountEntryViewModel> Accounts { get; } = [];
    public ObservableCollection<NetworkCacheShareEntryViewModel> Shares { get; } = [];
    public ObservableCollection<NetworkCacheScopeEntryViewModel> Scopes { get; } = [];

    /// <summary>
    /// Gets or sets whether the network cache is on; bound to the page header switch, and saved at once. Turning it on measures
    /// the shares, as opening the page does.
    /// </summary>
    public bool IsActive
    {
        get => configuration.Current.NetworkCache.IsEnabled;
        set
        {
            if (value == IsActive) return;
            Save(configuration.Current.NetworkCache with { IsEnabled = value });
            if (value) MeasureSpace();
        }
    }

    public string EnableLabel => localization.GetString("Nav_NetworkCacheKey.Title");
    public string DocumentationUrl => FoundryApplicationInfo.NetworkCacheDocumentationUrl;

    public string AccountsHeader => Text("AccountsHeader");
    public string AccountsDescription => Text("AccountsDescription");
    public string SharesHeader => Text("SharesHeader");
    public string SharesDescription => Text("SharesDescription");
    public string ScopesHeader => Text("ScopesHeader");
    public string ScopesDescription => Text("ScopesDescription");
    public string ScopesSpecificityText => Text("ScopesSpecificity");
    public string EmptyAccountsText => Text("EmptyAccounts");
    public string EmptySharesText => Text("EmptyShares");
    public string EmptyScopesText => Text(configuration.Current.NetworkCache.Shares.Count == 0 ? "ScopeNoShares" : "EmptyScopes");
    public string AddLabel => Text("CommandAdd");
    public string EditLabel => Text("CommandEdit");
    public string RemoveLabel => Text("CommandRemove");
    public string UserNameColumnHeader => Text("ColumnUserName");
    public string PasswordColumnHeader => Text("ColumnPassword");
    public string UsedByColumnHeader => Text("ColumnUsedBy");
    public string PathColumnHeader => Text("ColumnPath");
    public string AccountColumnHeader => Text("ColumnAccount");
    public string NameColumnHeader => Text("ColumnName");
    public string TypeColumnHeader => Text("ColumnType");
    public string ValueColumnHeader => Text("ColumnValue");
    public string SharesColumnHeader => Text("ColumnShares");
    public string StatusColumnHeader => Text("ColumnStatus");
    public string DownloadsColumnHeader => Text("ColumnDownloads");
    public string DownloadsColumnToolTip => Text("ColumnDownloadsToolTip");
    public string ImportedContentColumnHeader => Text("ColumnImportedContent");
    public string ImportedContentColumnToolTip => Text("ColumnImportedContentToolTip");
    public string RefreshSpaceLabel => localization.GetString("Common.Refresh");
    public string CleanUpShareLabel => Text("CommandCleanUp");
    public string PublishLabel => Text("CommandPublish");

    /// <summary>Gets whether Publish is shown: only a PC that writes the index publishes; a follower's shares come from its reference PC.</summary>
    public Visibility PublishVisibility => ToVisibility(publicationState.CanPublishIndex);

    /// <summary>
    /// Gets whether the page warns about the publication of valid settings of the active cache: they are not on every share yet,
    /// they cannot be signed, or media have no key to trust them. The page then asks to publish, or says why it cannot.
    /// </summary>
    public bool HasPublicationWarning => LacksTrustedKey ||
        (IsPublishable && (publicationState.Level != NetworkCachePublicationLevel.UpToDate || !publicationState.CanSignIndex));

    /// <summary>
    /// Gets the publication warning: that this PC has no key media can trust (a follower without a pinned reference key, or
    /// unavailable sharing), that this PC has no usable signing key, the shares whose last publication failed, the shares that
    /// lost the published content, or the shares as a whole when only changes are missing.
    /// </summary>
    public string PublicationWarningMessage => LacksTrustedKey && !publicationState.CanPublishIndex
        ? Text("TrustedKeyUnavailableMessage")
        : LacksTrustedKey || !publicationState.CanSignIndex
        ? Text("PublishKeysUnavailableMessage")
        : publicationState.FailedSharePaths is { Count: > 0 } failed
        ? localization.FormatString("NetworkCache.NotPublishedShareFormat", string.Join(", ", failed))
        : publicationState.MissingContentSharePaths is { Count: > 0 } emptied
        ? localization.FormatString("NetworkCache.ContentMissingShareFormat", string.Join(", ", emptied))
        : Text("NotPublishedMessage");

    public Visibility AccountListVisibility => ToVisibility(Accounts.Count > 0);
    public Visibility EmptyAccountsVisibility => ToVisibility(Accounts.Count == 0);
    public Visibility ShareListVisibility => ToVisibility(Shares.Count > 0);
    public Visibility EmptySharesVisibility => ToVisibility(Shares.Count == 0);
    public Visibility ScopeListVisibility => ToVisibility(Scopes.Count > 0);
    public Visibility EmptyScopesVisibility => ToVisibility(Scopes.Count == 0);

    /// <summary>Gets whether shares with an account are listed while Deploy password protection is not ready.</summary>
    public bool HasMediaProtectionIssue => HasIssue(NetworkCacheValidationCode.MediaProtectionRequired);
    public string MediaProtectionMessage => Text("Validation." + NetworkCacheValidationCode.MediaProtectionRequired);

    /// <summary>Gets what the share list as a whole still needs; problems of one share stay in its row.</summary>
    public string SharesValidationMessage => HasIssue(NetworkCacheValidationCode.SharesRequired)
        ? Text("Validation." + NetworkCacheValidationCode.SharesRequired) : string.Empty;
    public Visibility SharesValidationVisibility => ToVisibility(SharesValidationMessage.Length > 0);

    /// <summary>Gets what the scope list as a whole still needs; problems of one scope stay in its row.</summary>
    public string ScopesValidationMessage => HasIssue(NetworkCacheValidationCode.ScopesRequired)
        ? Text("Validation." + NetworkCacheValidationCode.ScopesRequired) : string.Empty;
    public Visibility ScopesValidationVisibility => ToVisibility(ScopesValidationMessage.Length > 0);

    [ObservableProperty]
    public partial NetworkCacheAccountEntryViewModel? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial NetworkCacheShareEntryViewModel? SelectedShare { get; set; }

    [ObservableProperty]
    public partial NetworkCacheScopeEntryViewModel? SelectedScope { get; set; }

    // A managed configuration keeps its published accounts, shares and scopes; editing an account then only enters its
    // password, and only when the publication does not include passwords.
    private bool IsEditable => !configuration.IsManaged;
    private bool CanAdd => IsEditable;
    private bool CanEditAccount => SelectedAccount is not null && (IsEditable || managed.ArePasswordsEditable);
    private bool CanRemoveAccount => IsEditable && SelectedAccount is not null;
    private bool CanEditShare => IsEditable && SelectedShare is not null;
    private bool CanAddScope => IsEditable && configuration.Current.NetworkCache.Shares.Count > 0;
    private bool CanEditScope => IsEditable && SelectedScope is not null;
    // Measuring only reads, so a PC that follows a managed configuration can measure the shares it cannot clean up.
    private bool CanRefreshSpace => Shares.Count > 0 && !IsShareOperationRunning;
    private bool CanCleanUpShare => IsEditable && SelectedShare is { IsReachable: true } && !IsShareOperationRunning;
    // Publishing needs valid shares and accounts, a signing key, and no other operation; it runs in the operation dialog,
    // which blocks the page meanwhile. It does not use the ADK, so a missing ADK does not block it.
    private bool IsPublishable => publicationState.CanPublishIndex && NetworkCacheContentPublisher.IsActive(configuration.Current.NetworkCache) &&
        configuration.NetworkCacheValidation.IsValid;
    private bool CanPublish => IsPublishable && publicationState.CanSignIndex && !IsShareOperationRunning &&
        navigationGuard.State is ShellNavigationState.Ready or ShellNavigationState.AdkBlocked;
    // Valid settings of the active cache that media cannot bind to a trusted index key: media creation refuses them.
    private bool LacksTrustedKey => NetworkCacheContentPublisher.IsActive(configuration.Current.NetworkCache) &&
        configuration.NetworkCacheValidation.IsValid && !publicationState.HasTrustedKey;


    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAccountAsync() =>
        networkCacheDialogs.ShowAccountAsync(new(IsNew: true, UserName: string.Empty), input => TrySaveAccount(null, input));

    [RelayCommand(CanExecute = nameof(CanEditAccount))]
    private Task EditAccountAsync()
    {
        if (SelectedAccount is not { } row) return Task.CompletedTask;
        NetworkCacheAccount account = row.Settings;
        return configuration.IsManaged
            ? networkCacheDialogs.ShowAccountAsync(new(IsNew: false, account.UserName, PasswordOnly: true, HasPassword: secrets.HasPassword(account.Id)), input => TrySavePassword(account.Id, input))
            : networkCacheDialogs.ShowAccountAsync(new(IsNew: false, account.UserName, HasPassword: secrets.HasPassword(account.Id)), input => TrySaveAccount(account.Id, input));
    }

    /// <summary>Adds or updates an account and its password; returns the reason shown in the dialog when the entry is refused.</summary>
    private string? TrySaveAccount(string? accountId, NetworkCacheAccountDialogInput input)
    {
        if (disposed) return null;
        string userName = input.UserName.Trim();
        if (userName.Length == 0) return Text("Validation." + NetworkCacheValidationCode.UserNameRequired);
        if (accountId is null && input.Password.IsEmpty) return Text("PasswordRequiredInput");
        if (GetPasswordRefusal(input) is { } refusal) return refusal;

        NetworkCacheSettings current = configuration.Current.NetworkCache;
        string id = accountId ?? Guid.NewGuid().ToString("N");
        var account = new NetworkCacheAccount { Id = id, UserName = userName };
        Save(current with
        {
            Accounts = accountId is null
                ? [.. current.Accounts, account]
                : current.Accounts.Select(existing => string.Equals(existing.Id, id, StringComparison.Ordinal) ? account : existing).ToArray()
        });

        // The account is saved first: the secret service erases passwords of accounts the settings do not have.
        // An empty password box keeps the password the account already owns.
        if (!input.Password.IsEmpty && !TrySetPassword(id, input.Password.Span))
        {
            // The dialog stays open on the refusal: a new account is removed again, so its next Save adds it only once.
            if (accountId is null) RemoveAccount(id);
            return Text("PasswordInvalid");
        }

        if (accountId is null) SelectedAccount = Accounts.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));
        return null;
    }

    /// <summary>Stores the password typed in the password-only account dialog; an empty box keeps the stored password.</summary>
    private string? TrySavePassword(string accountId, NetworkCacheAccountDialogInput input)
    {
        if (disposed) return null;
        if (GetPasswordRefusal(input) is { } refusal) return refusal;
        if (input.Password.IsEmpty) return null;
        return TrySetPassword(accountId, input.Password.Span) ? null : Text("PasswordInvalid");
    }

    /// <summary>Returns why the typed password cannot be stored, or null when it can (an empty pair included).</summary>
    private string? GetPasswordRefusal(NetworkCacheAccountDialogInput input)
    {
        if (!input.Password.Span.SequenceEqual(input.Confirmation.Span)) return Text("PasswordMismatch");
        ReadOnlySpan<char> password = input.Password.Span;
        bool isAccepted = password.IsEmpty || NetworkShareCredentialPayloadCodec.IsAcceptedPassword(password);
        return isAccepted ? null : Text("PasswordInvalid");
    }

    private bool TrySetPassword(string accountId, ReadOnlySpan<char> password)
    {
        try
        {
            secrets.SetPassword(accountId, password);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Removes the selected account, unless a share still opens with it.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAccount))]
    private async Task RemoveAccountAsync()
    {
        if (SelectedAccount is not { } row) return;
        NetworkCacheSettings current = configuration.Current.NetworkCache;
        if (current.Shares.Any(share => string.Equals(share.AccountId, row.Id, StringComparison.Ordinal)))
        {
            await dialogs.ShowMessageAsync(new DialogRequest(
                localization.FormatString("NetworkCache.AccountInUseTitleFormat", row.UserName),
                Text("AccountInUse"),
                localization.GetString("Common.Close")));
            return;
        }

        RemoveAccount(row.Id);
    }

    private void RemoveAccount(string accountId)
    {
        NetworkCacheSettings current = configuration.Current.NetworkCache;
        Save(current with { Accounts = current.Accounts.Where(account => !string.Equals(account.Id, accountId, StringComparison.Ordinal)).ToArray() });
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddShareAsync() =>
        networkCacheDialogs.ShowShareAsync(new(IsNew: true, Path: string.Empty, AccountId: null, GetAccountOptions()), input => TrySaveShare(null, input));

    [RelayCommand(CanExecute = nameof(CanEditShare))]
    private Task EditShareAsync()
    {
        if (SelectedShare is not { } row) return Task.CompletedTask;
        NetworkCacheShare share = row.Settings;
        return networkCacheDialogs.ShowShareAsync(new(IsNew: false, share.Path, share.AccountId, GetAccountOptions()), input => TrySaveShare(share.Id, input));
    }

    /// <summary>Adds or updates a share; returns the reason shown in the dialog when the entry is refused.</summary>
    private string? TrySaveShare(string? shareId, NetworkCacheShareDialogInput input)
    {
        if (disposed) return null;
        string path = input.Path.Trim();
        if (!NetworkCacheConfigurationValidator.IsValidSharePath(path)) return Text("Validation." + NetworkCacheValidationCode.InvalidSharePath);
        NetworkCacheSettings current = configuration.Current.NetworkCache;
        if (current.Shares.Any(share => !string.Equals(share.Id, shareId, StringComparison.Ordinal) && IsSamePath(share.Path, path)))
            return Text("ShareDuplicate");
        string? accountId = current.Accounts.Any(account => string.Equals(account.Id, input.AccountId, StringComparison.Ordinal)) ? input.AccountId : null;

        string id = shareId ?? Guid.NewGuid().ToString("N");
        var share = new NetworkCacheShare { Id = id, Path = path, AccountId = accountId };
        NetworkCacheShare? previous = current.Shares.FirstOrDefault(existing => string.Equals(existing.Id, id, StringComparison.Ordinal));
        Save(current with
        {
            Shares = shareId is null
                ? [.. current.Shares, share]
                : current.Shares.Select(existing => string.Equals(existing.Id, id, StringComparison.Ordinal) ? share : existing).ToArray()
        });
        if (shareId is null) SelectedShare = Shares.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));
        // A new share, or one that now points to another path or account, has no measurement yet: its row shows it at once.
        if (previous is null || !string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previous.AccountId, accountId, StringComparison.Ordinal))
            MeasureShare(id);
        return null;
    }

    /// <summary>Removes the selected share and, after confirmation, takes it out of the scopes that use it.</summary>
    [RelayCommand(CanExecute = nameof(CanEditShare))]
    private async Task RemoveShareAsync()
    {
        if (SelectedShare is not { } row) return;
        NetworkCacheSettings baseline = configuration.Current.NetworkCache;
        int scopeCount = baseline.Scopes.Count(scope => scope.ShareIds.Contains(row.Id, StringComparer.Ordinal));
        if (scopeCount > 0)
        {
            bool confirmed = await dialogs.ConfirmAsync(new ConfirmationDialogRequest(
                Text("RemoveShareTitle"),
                localization.FormatString("NetworkCache.RemoveShareUsedMessageFormat", row.Path, scopeCount),
                Text("CommandRemove"),
                localization.GetString("Common.Cancel"),
                PreferCancel: true));
            // The settings can change while the dialog is open, for example when the configuration is replaced.
            if (!confirmed || disposed || !ReferenceEquals(baseline, configuration.Current.NetworkCache)) return;
        }

        Save(baseline with
        {
            Shares = baseline.Shares.Where(share => !string.Equals(share.Id, row.Id, StringComparison.Ordinal)).ToArray(),
            Scopes = baseline.Scopes
                .Select(scope => scope with { ShareIds = scope.ShareIds.Where(id => !string.Equals(id, row.Id, StringComparison.Ordinal)).ToArray() })
                .ToArray()
        });
    }

    [RelayCommand(CanExecute = nameof(CanAddScope))]
    private Task AddScopeAsync() =>
        networkCacheDialogs.ShowScopeAsync(
            // With a single share, a new scope can only use that share, so it starts checked.
            new(IsNew: true, Name: string.Empty, NetworkCacheScopeKind.Subnet, Value: string.Empty,
                GetShareOptions(configuration.Current.NetworkCache.Shares is [{ } only] ? (string[])[only.Id] : (string[])[])),
            input => TrySaveScope(null, input));

    [RelayCommand(CanExecute = nameof(CanEditScope))]
    private Task EditScopeAsync()
    {
        if (SelectedScope is not { } row) return Task.CompletedTask;
        NetworkCacheScope scope = row.Settings;
        return networkCacheDialogs.ShowScopeAsync(new(IsNew: false, scope.Name, scope.Kind, scope.Value, GetShareOptions(scope.ShareIds)),
            input => TrySaveScope(scope.Id, input));
    }

    /// <summary>Adds or updates a scope; returns the reason shown in the dialog when the entry is refused.</summary>
    private string? TrySaveScope(string? scopeId, NetworkCacheScopeDialogInput input)
    {
        if (disposed) return null;
        string name = input.Name.Trim();
        string value = input.Value.Trim();
        if (name.Length == 0) return Text("ScopeNameRequired");
        if (!NetworkCacheScopeParser.TryParse(input.Kind, value, out _)) return Text("ScopeValueInvalid");
        if (input.ShareIds.Count == 0) return Text("ScopeSharesRequired");

        NetworkCacheSettings current = configuration.Current.NetworkCache;
        string id = scopeId ?? Guid.NewGuid().ToString("N");
        var scope = new NetworkCacheScope { Id = id, Name = name, Kind = input.Kind, Value = value, ShareIds = input.ShareIds.ToArray() };
        Save(current with
        {
            Scopes = scopeId is null
                ? [.. current.Scopes, scope]
                : current.Scopes.Select(existing => string.Equals(existing.Id, id, StringComparison.Ordinal) ? scope : existing).ToArray()
        });
        if (scopeId is null) SelectedScope = Scopes.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanEditScope))]
    private void RemoveScope()
    {
        if (SelectedScope is not { } row) return;
        NetworkCacheSettings current = configuration.Current.NetworkCache;
        Save(current with { Scopes = current.Scopes.Where(scope => !string.Equals(scope.Id, row.Id, StringComparison.Ordinal)).ToArray() });
    }

    /// <summary>Gets whether the space of the shares is being measured or a share is being cleaned up.</summary>
    [ObservableProperty]
    public partial bool IsShareOperationRunning { get; set; }

    /// <summary>
    /// Measures the shares of an active cache in the background, so their space shows without Refresh: when the page opens, when
    /// the cache is turned on and after Publish. The rows show Measuring meanwhile and the page stays usable; nothing starts
    /// while another share operation runs.
    /// </summary>
    public void MeasureSpace()
    {
        if (!disposed && IsActive && RefreshSpaceCommand.CanExecute(null)) RefreshSpaceCommand.Execute(null);
    }

    /// <summary>Measures the downloads and the imported content of every share, all shares at once, off the UI thread.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshSpace))]
    private Task RefreshSpaceAsync()
    {
        NetworkCacheSettings settings = configuration.Current.NetworkCache;
        return MeasureSharesAsync(settings, settings.Shares);
    }

    /// <summary>
    /// Measures one share of an active cache in the background, for a share added or changed while the page is open; nothing
    /// starts while another share operation runs, and Refresh measures it then.
    /// </summary>
    private void MeasureShare(string shareId)
    {
        NetworkCacheSettings settings = configuration.Current.NetworkCache;
        if (disposed || !IsActive || IsShareOperationRunning ||
            settings.Shares.FirstOrDefault(share => string.Equals(share.Id, shareId, StringComparison.Ordinal)) is not { } share)
            return;
        _ = MeasureSharesAsync(settings, (NetworkCacheShare[])[share]);
    }

    /// <summary>Measures the given shares as one share operation; leaving the page cancels it.</summary>
    private async Task MeasureSharesAsync(NetworkCacheSettings settings, IReadOnlyList<NetworkCacheShare> shares)
    {
        if (BeginShareOperation() is not { } operation) return;
        try
        {
            await Task.WhenAll(shares.Select(share => MeasureShareAsync(settings, share, operation.Token)));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        finally
        {
            EndShareOperation(operation);
        }
    }

    /// <summary>
    /// Publishes the imported content and the index of the current configuration to every share, without building media, then
    /// measures the shares again: their imported content changed, and the check confirms they hold the new publication.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        await publication.PublishAsync();
        MeasureSpace();
    }

    /// <summary>
    /// After confirmation, deletes the downloads of the selected share and the imported content that neither the current
    /// configuration nor any signed index on the share references, then measures the share again. Leaving the page cancels it;
    /// what was already deleted stays deleted.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCleanUpShare))]
    private async Task CleanUpShareAsync()
    {
        if (SelectedShare is not { } row) return;
        NetworkCacheSettings settings = configuration.Current.NetworkCache;
        NetworkCacheShare share = row.Settings;
        bool confirmed = await dialogs.ConfirmAsync(new ConfirmationDialogRequest(
            Text("CleanUpShareTitle"),
            localization.FormatString("NetworkCache.CleanUpShareMessageFormat", share.Path),
            Text("CommandCleanUp"),
            localization.GetString("Common.Cancel"),
            PreferCancel: true));
        // The settings can change while the dialog is open, for example when the configuration is replaced.
        if (!confirmed || disposed || !ReferenceEquals(settings, configuration.Current.NetworkCache)) return;
        if (BeginShareOperation() is not { } operation) return;
        FoundryConfigurationDocument current = configuration.Current;
        bool isCleanUpLogged = false;
        try
        {
            SetSpace(share, Text("SpaceCleaningUp"), isReachable: false);
            Logger.Information("Network cache share clean up started. SharePath={SharePath}", share.Path);
            NetworkCacheShareAccessResult<NetworkCacheCleanUpResult> result = await shareAccess.RunAsync(settings, share, secrets.GetPasswordCopy,
                (contentRoot, token) => NetworkCacheShareMaintenance.CleanUpAsync(contentRoot, current, token), operation.Token);
            isCleanUpLogged = true;
            if (result is { Status: NetworkCacheShareAccessStatus.Completed, Value: { } cleaned })
            {
                Logger.Information(
                    "Network cache share cleaned up. SharePath={SharePath}, RemovedImages={RemovedImages}, RemovedPackages={RemovedPackages}, RemovedManifests={RemovedManifests}, RetainedEntryCount={RetainedEntryCount}",
                    share.Path, cleaned.RemovedImages, cleaned.RemovedPackages, cleaned.RemovedManifests, cleaned.RetainedEntries.Count);
            }
            else
            {
                Logger.Warning(result.Error, "Network cache share could not be cleaned up. SharePath={SharePath}, Status={Status}, ConnectStatus={ConnectStatus}, Win32Error={Win32Error}",
                    share.Path, result.Status, result.Connection?.Status, result.Connection?.Win32Error);
            }

            if (disposed) return;
            await MeasureShareAsync(settings, share, operation.Token);
            if (disposed) return;
            string? message = result.Status != NetworkCacheShareAccessStatus.Completed
                ? localization.FormatString("NetworkCache.CleanUpShareFailedMessageFormat", share.Path)
                : result.Value?.RetainedEntries.Count > 0 ? localization.FormatString("NetworkCache.CleanUpShareKeptMessageFormat", share.Path) : null;
            if (message is not null)
                await dialogs.ShowMessageAsync(new DialogRequest(Text("CleanUpShareTitle"), message, localization.GetString("Common.Close")));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // Leaving the page cancels a clean up midway; the files it already deleted stay deleted.
            if (!isCleanUpLogged) Logger.Information("Network cache share clean up cancelled. SharePath={SharePath}", share.Path);
        }
        finally
        {
            EndShareOperation(operation);
        }
    }

    /// <summary>Measures one share and shows the result in its row; an unreachable or unreadable share disables cleaning it up.</summary>
    private async Task MeasureShareAsync(NetworkCacheSettings settings, NetworkCacheShare share, CancellationToken cancellationToken)
    {
        SetSpace(share, Text("SpaceMeasuring"), isReachable: false);
        NetworkCacheShareExpectation? expectation = publicationState.GetExpectation(share.Id);
        NetworkCacheShareAccessResult<ShareMeasurement> result = await shareAccess.RunAsync(settings, share, secrets.GetPasswordCopy,
            (contentRoot, token) => MeasureAndCheckAsync(contentRoot, expectation, token), cancellationToken);
        if (result is { Status: NetworkCacheShareAccessStatus.Completed, Value: { Usage: var usage, IsIntact: var isIntact } })
        {
            if (isIntact is { } intact)
            {
                if (!intact) Logger.Warning("Network cache share no longer holds the published content. SharePath={SharePath}", share.Path);
                publicationState.ReportContentCheck(share.Id, intact);
            }

            Logger.Debug("Network cache share measured. SharePath={SharePath}, DownloadsBytes={DownloadsBytes}, ImportedContentBytes={ImportedContentBytes}",
                share.Path, usage.DownloadsBytes, usage.ImportedContentBytes);
            SetSpace(share, ByteSizeFormatter.Format(localization, (ulong)usage.DownloadsBytes),
                ByteSizeFormatter.Format(localization, (ulong)usage.ImportedContentBytes), isReachable: true,
                usage.DownloadsBytes, usage.ImportedContentBytes);
            return;
        }

        Logger.Warning(result.Error, "Network cache share could not be measured. SharePath={SharePath}, Status={Status}, ConnectStatus={ConnectStatus}, Win32Error={Win32Error}",
            share.Path, result.Status, result.Connection?.Status, result.Connection?.Win32Error);
        SetSpace(share, Text(result.Status == NetworkCacheShareAccessStatus.Unreachable ? "SpaceUnreachable" : "SpaceUnreadable"), isReachable: false);
    }

    /// <summary>
    /// Measures a share and, when this PC published the latest changes to it, checks that it still holds them; a check the share
    /// cannot answer, for any reason other than cancellation, leaves the publication state as it was and keeps the measurement.
    /// </summary>
    private static async Task<ShareMeasurement> MeasureAndCheckAsync(string contentRoot, NetworkCacheShareExpectation? expectation, CancellationToken cancellationToken)
    {
        NetworkCacheShareUsage usage = await NetworkCacheShareMaintenance.MeasureAsync(contentRoot, cancellationToken);
        if (expectation is null) return new(usage, null);
        try
        {
            return new(usage, await NetworkCachePublicationCheck.IsIntactAsync(contentRoot, expectation.PublicKey, expectation.Sequence, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Network cache share content could not be checked. ContentRoot={ContentRoot}", contentRoot);
            return new(usage, null);
        }
    }

    /// <summary>The space a share uses and, when it was checked, whether it still holds what this PC published.</summary>
    private sealed record ShareMeasurement(NetworkCacheShareUsage Usage, bool? IsIntact);

    /// <summary>Shows the same progress or failure text in both space columns of the share, whose sizes are then unknown.</summary>
    private void SetSpace(NetworkCacheShare share, string text, bool isReachable) => SetSpace(share, text, text, isReachable, null, null);

    /// <summary>Updates the row of the measured share, unless it was removed or now points to another path or account.</summary>
    private void SetSpace(NetworkCacheShare share, string downloadsText, string importedContentText, bool isReachable,
        long? downloadsBytes, long? importedContentBytes)
    {
        if (disposed) return;
        NetworkCacheShareEntryViewModel? row = Shares.FirstOrDefault(candidate => string.Equals(candidate.Id, share.Id, StringComparison.Ordinal));
        if (row is null || !string.Equals(row.Settings.Path, share.Path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(row.Settings.AccountId, share.AccountId, StringComparison.Ordinal))
            return;
        row.DownloadsText = downloadsText;
        row.ImportedContentText = importedContentText;
        row.DownloadsBytes = downloadsBytes;
        row.ImportedContentBytes = importedContentBytes;
        row.IsReachable = isReachable;
        CleanUpShareCommand.NotifyCanExecuteChanged();
    }

    private CancellationTokenSource? BeginShareOperation()
    {
        if (disposed || IsShareOperationRunning) return null;
        shareOperation = new CancellationTokenSource();
        IsShareOperationRunning = true;
        return shareOperation;
    }

    private void EndShareOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(shareOperation, operation)) shareOperation = null;
        operation.Dispose();
        if (!disposed) IsShareOperationRunning = false;
    }

    partial void OnIsShareOperationRunningChanged(bool value)
    {
        RefreshSpaceCommand.NotifyCanExecuteChanged();
        CleanUpShareCommand.NotifyCanExecuteChanged();
        PublishCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Lists "No account" first, then the accounts by user name.</summary>
    private NetworkCacheAccountOption[] GetAccountOptions() =>
    [
        new(null, Text("NoAccount")),
        .. configuration.Current.NetworkCache.Accounts
            .OrderBy(account => account.UserName, StringComparer.CurrentCultureIgnoreCase)
            .Select(account => new NetworkCacheAccountOption(account.Id, account.UserName))
    ];

    /// <summary>Lists the scope's shares first, in their try order, then every other share unchecked.</summary>
    private NetworkCacheScopeShareOption[] GetShareOptions(IReadOnlyList<string> scopeShareIds)
    {
        IReadOnlyList<NetworkCacheShare> shares = configuration.Current.NetworkCache.Shares;
        IEnumerable<NetworkCacheShareOption> included = scopeShareIds
            .Select(id => shares.FirstOrDefault(share => string.Equals(share.Id, id, StringComparison.Ordinal)))
            .OfType<NetworkCacheShare>()
            .Select(share => new NetworkCacheShareOption(share, true));
        IEnumerable<NetworkCacheShareOption> others = shares
            .Where(share => !scopeShareIds.Contains(share.Id, StringComparer.Ordinal))
            .Select(share => new NetworkCacheShareOption(share, false));
        return included.Concat(others).Select(option => new NetworkCacheScopeShareOption(option.Share.Id, option.Share.Path, option.IsIncluded)).ToArray();
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(left.Trim().TrimEnd('\\'), right.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void Save(NetworkCacheSettings settings)
    {
        // A managed configuration keeps its published section; only account passwords are entered while it is managed.
        if (configuration.IsManaged) return;
        configuration.UpdateNetworkCache(settings);
    }

    private string Text(string key) => localization.GetString("NetworkCache." + key);
    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
    private bool HasIssue(NetworkCacheValidationCode code) => issues.Any(issue => issue.Code == code);

    partial void OnSelectedAccountChanged(NetworkCacheAccountEntryViewModel? value)
    {
        EditAccountCommand.NotifyCanExecuteChanged();
        RemoveAccountCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedShareChanged(NetworkCacheShareEntryViewModel? value)
    {
        EditShareCommand.NotifyCanExecuteChanged();
        RemoveShareCommand.NotifyCanExecuteChanged();
        CleanUpShareCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedScopeChanged(NetworkCacheScopeEntryViewModel? value)
    {
        EditScopeCommand.NotifyCanExecuteChanged();
        RemoveScopeCommand.NotifyCanExecuteChanged();
    }

    private void OnStateChanged(object? sender, EventArgs args) => ApplyState();

    private void OnManagedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => RefreshPresentation();

    private void OnSecretsChanged(object? sender, EventArgs args) => RefreshPresentation();

    private void OnPublicationChanged(object? sender, EventArgs args) => RefreshPresentation();

    /// <summary>Publish waits for any running operation, media creation included, which the navigation guard reports.</summary>
    private void OnNavigationGuardChanged(object? sender, EventArgs args)
    {
        if (dispatcherQueue?.HasThreadAccess == false)
        {
            dispatcherQueue.TryEnqueue(RefreshPublishCommand);
            return;
        }

        RefreshPublishCommand();
    }

    private void RefreshPublishCommand()
    {
        if (!disposed) PublishCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        // Every text of the page comes from resources; an empty name refreshes all bindings.
        OnPropertyChanged(string.Empty);
        RefreshPresentation();
    }

    private void ApplyState()
    {
        if (disposed || applying) return;
        applying = true;
        try
        {
            NetworkCacheSettings settings = configuration.Current.NetworkCache;
            Synchronize(Accounts, settings.Accounts, row => row.Id, account => account.Id, account => new(account),
                (row, account) => row.Settings = account, () => AccountRowsRemoving?.Invoke(this, EventArgs.Empty));
            Synchronize(Shares, settings.Shares, row => row.Id, share => share.Id, share => new(share),
                (row, share) => row.Settings = share, () => ShareRowsRemoving?.Invoke(this, EventArgs.Empty));
            Synchronize(Scopes, settings.Scopes, row => row.Id, scope => scope.Id, scope => new(scope),
                (row, scope) => row.Settings = scope, () => ScopeRowsRemoving?.Invoke(this, EventArgs.Empty));
            if (SelectedAccount is not null && !Accounts.Contains(SelectedAccount)) SelectedAccount = null;
            if (SelectedShare is not null && !Shares.Contains(SelectedShare)) SelectedShare = null;
            if (SelectedScope is not null && !Scopes.Contains(SelectedScope)) SelectedScope = null;
        }
        finally { applying = false; }
        RefreshPresentation();
    }

    /// <summary>
    /// Aligns table rows with the saved items while keeping the row of every item that still exists, so the selection
    /// survives a save.
    /// </summary>
    private static void Synchronize<TRow, TItem>(ObservableCollection<TRow> rows, IReadOnlyList<TItem> items, Func<TRow, string> rowId,
        Func<TItem, string> itemId, Func<TItem, TRow> create, Action<TRow, TItem> update, Action raiseRemoving)
    {
        var ids = new HashSet<string>(items.Select(itemId), StringComparer.Ordinal);
        if (rows.Any(row => !ids.Contains(rowId(row)))) raiseRemoving();
        for (int index = rows.Count - 1; index >= 0; index--)
        {
            if (!ids.Contains(rowId(rows[index]))) rows.RemoveAt(index);
        }

        for (int index = 0; index < items.Count; index++)
        {
            string id = itemId(items[index]);
            int existing = -1;
            for (int candidate = index; candidate < rows.Count && existing < 0; candidate++)
            {
                if (string.Equals(rowId(rows[candidate]), id, StringComparison.Ordinal)) existing = candidate;
            }

            if (existing < 0) rows.Insert(index, create(items[index]));
            else
            {
                if (existing != index) rows.Move(existing, index);
                update(rows[index], items[index]);
            }
        }
    }

    /// <summary>Recomputes readiness and the derived text of every row; a disabled section reports no issue.</summary>
    private void RefreshValidation()
    {
        NetworkCacheSettings settings = configuration.Current.NetworkCache;
        issues = configuration.NetworkCacheValidation.Issues;
        string readyText = Text("Ready");
        string savedText = Text("PasswordSaved");
        string missingText = Text("PasswordMissing");
        string noAccountText = Text("NoAccount");
        string upToDateText = Text("UpToDateState");
        string notPublishedText = Text("NotPublishedState");
        string outdatedText = Text("OutdatedState");
        // A valid share reports whether it has the latest changes, on the PC that publishes them; a follower shows Ready.
        bool showsPublication = publicationState.CanPublishIndex && NetworkCacheContentPublisher.IsActive(settings);
        Dictionary<string, string> userNames = settings.Accounts
            .GroupBy(account => account.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().UserName, StringComparer.Ordinal);
        Dictionary<string, string> paths = settings.Shares
            .GroupBy(share => share.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.Ordinal);

        foreach (NetworkCacheAccountEntryViewModel row in Accounts)
        {
            row.PasswordText = secrets.HasPassword(row.Id) ? savedText : missingText;
            row.UsedByCount = settings.Shares.Count(share => string.Equals(share.AccountId, row.Id, StringComparison.Ordinal));
            NetworkCacheValidationIssue? issue = issues.FirstOrDefault(candidate => string.Equals(candidate.ItemId, row.Id, StringComparison.Ordinal));
            row.IsReady = issue is null;
            row.StatusText = issue is null ? readyText : Text("Validation." + issue.Code);
        }

        foreach (NetworkCacheShareEntryViewModel row in Shares)
        {
            string? accountId = row.Settings.AccountId;
            row.AccountText = accountId is null ? noAccountText : userNames.GetValueOrDefault(accountId, accountId);
            // A missing password belongs to the account; the shares that open with it report it too.
            NetworkCacheValidationIssue? issue = issues.FirstOrDefault(candidate =>
                string.Equals(candidate.ItemId, row.Id, StringComparison.Ordinal) ||
                (candidate.Code == NetworkCacheValidationCode.PasswordRequired && accountId is not null &&
                 string.Equals(candidate.ItemId, accountId, StringComparison.Ordinal)));
            row.IsReady = issue is null;
            // The row tells only whether the share holds the latest publication; a signing key problem is the warning bar's.
            NetworkCachePublicationLevel level = publicationState.LevelOf(row.Id);
            row.NeedsPublication = issue is null && showsPublication && level != NetworkCachePublicationLevel.UpToDate;
            row.IsUnpublished = row.NeedsPublication && level == NetworkCachePublicationLevel.NotPublished;
            row.StatusText = issue is not null ? Text("Validation." + issue.Code)
                : !showsPublication ? readyText
                : row.IsUnpublished ? notPublishedText
                : row.NeedsPublication ? outdatedText : upToDateText;
        }

        foreach (NetworkCacheScopeEntryViewModel row in Scopes)
        {
            row.KindText = Text("ScopeKind." + row.Settings.Kind);
            row.SharesText = string.Join(ShareSeparator, row.Settings.ShareIds.Where(paths.ContainsKey).Select(id => paths[id]));
            NetworkCacheValidationIssue? issue = issues.FirstOrDefault(candidate => string.Equals(candidate.ItemId, row.Id, StringComparison.Ordinal));
            row.IsReady = issue is null;
            row.StatusText = issue is null ? readyText : Text("Validation." + issue.Code);
        }
    }

    private void RefreshPresentation()
    {
        // Disposal can follow application shutdown, when the shared secret services are already disposed.
        if (disposed) return;
        RefreshValidation();
        foreach (string property in new[]
        {
            nameof(IsActive), nameof(EmptyScopesText), nameof(AccountListVisibility), nameof(EmptyAccountsVisibility),
            nameof(ShareListVisibility), nameof(EmptySharesVisibility), nameof(ScopeListVisibility), nameof(EmptyScopesVisibility),
            nameof(HasMediaProtectionIssue), nameof(SharesValidationMessage), nameof(SharesValidationVisibility),
            nameof(ScopesValidationMessage), nameof(ScopesValidationVisibility), nameof(PublishVisibility), nameof(HasPublicationWarning),
            nameof(PublicationWarningMessage)
        })
        {
            OnPropertyChanged(property);
        }

        AddAccountCommand.NotifyCanExecuteChanged();
        EditAccountCommand.NotifyCanExecuteChanged();
        RemoveAccountCommand.NotifyCanExecuteChanged();
        AddShareCommand.NotifyCanExecuteChanged();
        EditShareCommand.NotifyCanExecuteChanged();
        RemoveShareCommand.NotifyCanExecuteChanged();
        RefreshSpaceCommand.NotifyCanExecuteChanged();
        CleanUpShareCommand.NotifyCanExecuteChanged();
        PublishCommand.NotifyCanExecuteChanged();
        AddScopeCommand.NotifyCanExecuteChanged();
        EditScopeCommand.NotifyCanExecuteChanged();
        RemoveScopeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drops subscriptions without clearing passwords owned by the configuration session.</summary>
    public void Dispose()
    {
        disposed = true;
        shareOperation?.Cancel();
        configuration.StateChanged -= OnStateChanged;
        managed.PropertyChanged -= OnManagedChanged;
        secrets.Changed -= OnSecretsChanged;
        protectionSecrets.Changed -= OnSecretsChanged;
        localization.LanguageChanged -= OnLanguageChanged;
        publicationState.Changed -= OnPublicationChanged;
        navigationGuard.StateChanged -= OnNavigationGuardChanged;
    }

    private sealed record NetworkCacheShareOption(NetworkCacheShare Share, bool IsIncluded);
}
