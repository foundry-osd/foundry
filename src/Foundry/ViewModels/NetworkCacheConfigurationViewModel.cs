// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.NetworkCache;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

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
    private IReadOnlyList<NetworkCacheValidationIssue> issues = [];
    private bool applying;
    private bool disposed;

    public NetworkCacheConfigurationViewModel(IFoundryConfigurationStateService configuration, INetworkCacheSecretStateService secrets,
        IDeploymentProtectionSecretStateService protectionSecrets, INetworkCacheDialogService networkCacheDialogs, IDialogService dialogs,
        IApplicationLocalizationService localization, ManagedConfigurationViewModel managed)
    {
        this.configuration = configuration;
        this.managed = managed;
        this.secrets = secrets;
        this.protectionSecrets = protectionSecrets;
        this.networkCacheDialogs = networkCacheDialogs;
        this.dialogs = dialogs;
        this.localization = localization;
        configuration.StateChanged += OnStateChanged;
        managed.PropertyChanged += OnManagedChanged;
        secrets.Changed += OnSecretsChanged;
        protectionSecrets.Changed += OnSecretsChanged;
        localization.LanguageChanged += OnLanguageChanged;
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

    public bool IsActive => configuration.Current.NetworkCache.IsEnabled;
    public string ActionText => localization.GetString(IsActive ? "Common.Disable" : "Common.Enable");
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

    [RelayCommand]
    private void ToggleEnabled()
    {
        NetworkCacheSettings current = configuration.Current.NetworkCache;
        Save(current with { IsEnabled = !current.IsEnabled });
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddAccountAsync() =>
        networkCacheDialogs.ShowAccountAsync(new(IsNew: true, UserName: string.Empty), input => TrySaveAccount(null, input));

    [RelayCommand(CanExecute = nameof(CanEditAccount))]
    private Task EditAccountAsync()
    {
        if (SelectedAccount is not { } row) return Task.CompletedTask;
        NetworkCacheAccount account = row.Settings;
        return configuration.IsManaged
            ? networkCacheDialogs.ShowAccountAsync(new(IsNew: false, account.UserName, PasswordOnly: true), input => TrySavePassword(account.Id, input))
            : networkCacheDialogs.ShowAccountAsync(new(IsNew: false, account.UserName), input => TrySaveAccount(account.Id, input));
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
        if (!input.Password.IsEmpty && !TrySetPassword(id, input.Password.Span)) return Text("PasswordInvalid");
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
        bool isAccepted = password.IsEmpty ||
            (!password.Contains('\0') && Encoding.UTF8.GetByteCount(password) <= NetworkShareCredentialPayloadCodec.MaximumPasswordUtf8Bytes);
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

        Save(current with { Accounts = current.Accounts.Where(account => !string.Equals(account.Id, row.Id, StringComparison.Ordinal)).ToArray() });
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
        Save(current with
        {
            Shares = shareId is null
                ? [.. current.Shares, share]
                : current.Shares.Select(existing => string.Equals(existing.Id, id, StringComparison.Ordinal) ? share : existing).ToArray()
        });
        if (shareId is null) SelectedShare = Shares.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));
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
        networkCacheDialogs.ShowScopeAsync(new(IsNew: true, Name: string.Empty, NetworkCacheScopeKind.Subnet, Value: string.Empty, GetShareOptions([])),
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
    }

    partial void OnSelectedScopeChanged(NetworkCacheScopeEntryViewModel? value)
    {
        EditScopeCommand.NotifyCanExecuteChanged();
        RemoveScopeCommand.NotifyCanExecuteChanged();
    }

    private void OnStateChanged(object? sender, EventArgs args) => ApplyState();

    private void OnManagedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => RefreshPresentation();

    private void OnSecretsChanged(object? sender, EventArgs args) => RefreshPresentation();

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
            row.StatusText = issue is null ? readyText : Text("Validation." + issue.Code);
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
            nameof(IsActive), nameof(ActionText), nameof(EmptyScopesText), nameof(AccountListVisibility), nameof(EmptyAccountsVisibility),
            nameof(ShareListVisibility), nameof(EmptySharesVisibility), nameof(ScopeListVisibility), nameof(EmptyScopesVisibility),
            nameof(HasMediaProtectionIssue), nameof(SharesValidationMessage), nameof(SharesValidationVisibility),
            nameof(ScopesValidationMessage), nameof(ScopesValidationVisibility)
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
        AddScopeCommand.NotifyCanExecuteChanged();
        EditScopeCommand.NotifyCanExecuteChanged();
        RemoveScopeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drops subscriptions without clearing passwords owned by the configuration session.</summary>
    public void Dispose()
    {
        disposed = true;
        configuration.StateChanged -= OnStateChanged;
        managed.PropertyChanged -= OnManagedChanged;
        secrets.Changed -= OnSecretsChanged;
        protectionSecrets.Changed -= OnSecretsChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }

    private sealed record NetworkCacheShareOption(NetworkCacheShare Share, bool IsIncluded);
}
