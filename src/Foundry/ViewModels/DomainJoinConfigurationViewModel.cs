// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.DomainJoin;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Shares the list of joinable domains, their OUs and the volatile join account passwords across both authoring
/// pages. The OU list always shows the domain selected in the domain list.
/// </summary>
public sealed partial class DomainJoinConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IDomainJoinSecretStateService secrets;
    private readonly IDeploymentProtectionSecretStateService protectionSecrets;
    private readonly IAuthoringDomainOuDiscoveryService discovery;
    private readonly IDomainJoinDialogService domainDialogs;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedOuRows = [];
    private IReadOnlyList<DomainJoinValidationIssue> issues = [];
    private CancellationTokenSource? discoveryCancellation;
    private DomainJoinSettings? appliedSettings;
    private DomainJoinMode pageMode;
    private bool applying;
    private bool disposed;
    private bool sharedCredentialInputInvalid;
    private long discoveryRevision;
    private string? statusKey;

    public DomainJoinConfigurationViewModel(IFoundryConfigurationStateService configuration,
        IDomainJoinSecretStateService secrets, IDeploymentProtectionSecretStateService protectionSecrets, IAuthoringDomainOuDiscoveryService discovery,
        IDomainJoinDialogService domainDialogs, IDialogService dialogs, IApplicationLocalizationService localization)
    {
        this.configuration = configuration;
        this.secrets = secrets;
        this.protectionSecrets = protectionSecrets;
        this.discovery = discovery;
        this.domainDialogs = domainDialogs;
        this.dialogs = dialogs;
        this.localization = localization;
        configuration.StateChanged += OnStateChanged;
        secrets.Changed += OnSecretsChanged;
        protectionSecrets.Changed += OnProtectionSecretsChanged;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyState();
    }

    /// <summary>
    /// Raised before listed rows are removed. A table must drop its selection first: removing selected rows one
    /// by one makes it scroll to a row that no longer exists.
    /// </summary>
    public event EventHandler? DomainRowsRemoving;

    /// <inheritdoc cref="DomainRowsRemoving"/>
    public event EventHandler? OrganizationalUnitRowsRemoving;

    public ObservableCollection<DomainJoinDomainEntryViewModel> Domains { get; } = [];

    /// <summary>Gets the OUs of <see cref="SelectedDomain"/>; a domain search changes them only through the import dialog.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> OrganizationalUnits { get; } = [];

    public bool IsActive => configuration.Current.DomainJoin.IsEnabled && configuration.Current.DomainJoin.Mode == pageMode;
    public bool IsZeroTouch => pageMode == DomainJoinMode.Automatic;
    public string ActionText => localization.GetString(IsActive ? "Common.Disable" : "Common.Enable");
    public string DocumentationUrl => pageMode == DomainJoinMode.Interactive
        ? FoundryApplicationInfo.InteractiveDomainJoinDocumentationUrl : FoundryApplicationInfo.ZeroTouchDomainJoinDocumentationUrl;

    public string DomainColumnHeader => Text("ColumnDomain");
    public string AccountColumnHeader => Text("ColumnAccount");
    public string OuCountColumnHeader => Text("ColumnOuCount");
    public string DefaultColumnHeader => Text("ColumnDefault");
    public string StatusColumnHeader => Text("ColumnStatus");
    public string LabelColumnHeader => localization.GetString("DomainJoinManualLabel.Header");
    public string DistinguishedNameColumnHeader => localization.GetString("DomainJoinManualDn.Header");
    public string EmptyDomainsText => Text("EmptyDomains");
    public string EmptyOrganizationalUnitsText => Text(HasSelectedDomain ? "EmptyOrganizationalUnits" : Domains.Count == 0 ? "NoDomainYet" : "SelectDomain");
    public string AddLabel => Text("CommandAdd");
    public string EditLabel => Text("CommandEdit");
    public string RemoveLabel => Text("CommandRemove");
    public string SetDefaultLabel => Text("CommandSetDefault");
    public string ClearDefaultLabel => Text("CommandClearDefault");

    /// <summary>Gets the import button label; the same button cancels a running domain search.</summary>
    public string ImportButtonText => localization.GetString(IsDiscovering ? "DomainJoinCancel.Content" : "DomainJoinDiscover.Content");

    public bool HasSelectedDomain => SelectedDomain is not null;
    public string OrganizationalUnitsHeader => SelectedDomain is { } domain
        ? localization.FormatString("DomainJoin.OrganizationalUnitsOfFormat", domain.DomainName)
        : Text("OrganizationalUnitsHeader");
    public Visibility DomainListVisibility => ToVisibility(Domains.Count > 0);
    public Visibility EmptyDomainsVisibility => ToVisibility(Domains.Count == 0);
    public Visibility OrganizationalUnitListVisibility => ToVisibility(OrganizationalUnits.Count > 0);
    public Visibility EmptyOrganizationalUnitsVisibility => ToVisibility(OrganizationalUnits.Count == 0);

    public string SharedAccountValidationMessage => sharedCredentialInputInvalid ? Text("CredentialInputInvalid") : GetSharedAccountIssueText();
    public Visibility SharedAccountValidationVisibility => ToVisibility(SharedAccountValidationMessage.Length > 0);
    public string DomainsValidationMessage => GetIssueText(issue => issue.DomainId is null && IsDomainListIssue(issue.Code));
    public Visibility DomainsValidationVisibility => ToVisibility(DomainsValidationMessage.Length > 0);
    public string OrganizationalUnitsValidationMessage => SelectedDomain is { } domain
        ? GetIssueText(issue => string.Equals(issue.DomainId, domain.Id, StringComparison.OrdinalIgnoreCase) && IsOrganizationalUnitIssue(issue.Code))
        : string.Empty;
    public Visibility OrganizationalUnitsValidationVisibility => ToVisibility(OrganizationalUnitsValidationMessage.Length > 0);

    /// <summary>Gets the progress or outcome of the last domain search, shown beside the OU commands.</summary>
    public string StatusText => statusKey is null ? string.Empty : Text(statusKey);
    public Visibility StatusVisibility => ToVisibility(statusKey is not null);
    public Visibility DiscoveringVisibility => ToVisibility(IsDiscovering);

    private bool CanEditDomain => SelectedDomain is not null;
    private bool CanSetDefaultDomain => SelectedDomain is { } domain &&
        !string.Equals(configuration.Current.DomainJoin.DefaultDomainId, domain.Id, StringComparison.OrdinalIgnoreCase);
    private bool CanEditOrganizationalUnit => SelectedDomain is not null && selectedOuRows.Count == 1;
    private bool CanRemoveSelectedOrganizationalUnits => selectedOuRows.Count > 0;
    // A domain's only OU is always used, so the default only matters, and can only change, among several.
    private bool CanSetDefaultOrganizationalUnit => SelectedDomain is { } domain && selectedOuRows.Count == 1 &&
        !string.Equals(GetDefaultOrganizationalUnitId(domain.Settings), selectedOuRows[0].Settings.Id, StringComparison.OrdinalIgnoreCase);
    private bool CanClearDefaultOrganizationalUnit => SelectedDomain?.Settings is { DefaultOuId: not null, OrganizationalUnits.Count: > 1 };

    [ObservableProperty]
    public partial DomainJoinDomainEntryViewModel? SelectedDomain { get; set; }

    /// <summary>Gets or sets the account used by every domain that has no account of its own.</summary>
    [ObservableProperty]
    public partial string SharedAccountName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDiscovering { get; set; }

    /// <summary>Invalidates PasswordBox content when volatile secret ownership or profile state changes.</summary>
    [ObservableProperty]
    public partial int SecretStateVersion { get; set; }

    /// <summary>Selects the page presentation without changing the configured provisioning mode.</summary>
    public void SetPageMode(DomainJoinMode mode)
    {
        pageMode = mode;
        OnPropertyChanged(nameof(IsZeroTouch));
        RefreshPresentation();
    }

    [RelayCommand]
    private async Task ToggleModeAsync()
    {
        FoundryConfigurationDocument baseline = configuration.Current;
        ProvisioningSelection requested = pageMode switch
        {
            DomainJoinMode.Interactive => ProvisioningSelection.DomainJoinInteractive,
            DomainJoinMode.Automatic => ProvisioningSelection.DomainJoinAutomatic,
            _ => throw new ArgumentOutOfRangeException(nameof(pageMode))
        };
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(baseline.Autopilot, baseline.DomainJoin, requested);
        if (decision.RequiresReplacementConfirmation &&
            !await ProvisioningModeReplacementConfirmation.ConfirmAsync(dialogs, localization,
                ProvisioningModeSelectionEvaluator.GetCurrent(baseline.Autopilot, baseline.DomainJoin), requested))
        {
            return;
        }

        if (disposed || !ReferenceEquals(baseline, configuration.Current)) return;
        configuration.UpdateProvisioningSelection(baseline.Autopilot with { IsEnabled = false },
            baseline.DomainJoin with { IsEnabled = decision.Next == requested, Mode = pageMode });
    }

    /// <summary>Copies the shared account's password for PasswordBox synchronization; the caller clears the buffer.</summary>
    public char[]? GetSharedPasswordCopy() => IsActive && IsZeroTouch && SharedAccountName.Trim() is { Length: > 0 } account
        ? secrets.GetPasswordCopy(account) : null;

    /// <summary>Stores the shared account's password; an account that is not qualified cannot own one.</summary>
    public void SetSharedPassword(ReadOnlySpan<char> password)
    {
        if (!IsActive || !IsZeroTouch) return;
        try
        {
            secrets.SetPassword(SharedAccountName.Trim(), password);
            SetSharedCredentialInputInvalid(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            // An empty box on an account that cannot own a password is not an error worth showing.
            SetSharedCredentialInputInvalid(!password.IsEmpty);
        }
    }

    [RelayCommand]
    private Task AddDomainAsync() => domainDialogs.ShowDomainAsync(
        new(IsNew: true, AsksForAccount: IsZeroTouch, CanRename: true, DomainName: string.Empty, AccountName: null),
        input => TrySaveDomain(null, input));

    [RelayCommand(CanExecute = nameof(CanEditDomain))]
    private Task EditDomainAsync()
    {
        if (SelectedDomain is not { } row) return Task.CompletedTask;
        DomainJoinDomainSettings domain = row.Settings;
        return domainDialogs.ShowDomainAsync(
            new(IsNew: false, AsksForAccount: IsZeroTouch, CanRename: domain.OrganizationalUnits.Count == 0, domain.DomainName, domain.AccountName),
            input => TrySaveDomain(domain.Id, input));
    }

    /// <summary>Adds or updates a domain; returns the reason shown in the dialog when the entry is refused.</summary>
    private string? TrySaveDomain(string? domainId, DomainJoinDomainDialogInput input)
    {
        if (disposed) return null;
        DomainJoinSettings current = configuration.Current.DomainJoin;
        string? account = IsZeroTouch && !input.UsesSharedAccount ? input.AccountName.Trim() : null;
        if (account is not null && !DomainJoinConfigurationValidator.IsQualifiedAccount(account))
            return Text("Validation." + DomainJoinValidationCode.QualifiedAccountRequired);
        if (account is not null && !input.Password.IsEmpty)
        {
            try { DomainJoinCredentialPayloadCodec.ValidatePassword(input.Password.Span); }
            catch (InvalidDataException) { return Text("CredentialInputInvalid"); }
        }

        try
        {
            // Interactive hides the account inputs, so an edit there keeps the account Zero-touch stored on the domain.
            Save(domainId is null ? DomainJoinDomainCatalog.Add(current, input.DomainName, account)
                : IsZeroTouch ? DomainJoinDomainCatalog.Update(current, domainId, input.DomainName, account)
                : DomainJoinDomainCatalog.Rename(current, domainId, input.DomainName));
        }
        catch (ArgumentException) { return Text(GetDomainFailureKey(current, domainId, input.DomainName)); }

        // An empty password box keeps the password the account already owns.
        if (account is not null && !input.Password.IsEmpty) secrets.SetPassword(account, input.Password.Span);
        if (domainId is null) SelectedDomain = Domains.LastOrDefault();
        return null;
    }

    private static string GetDomainFailureKey(DomainJoinSettings current, string? domainId, string domainName)
    {
        string name = (domainName ?? string.Empty).Trim();
        if (!DomainJoinCredentialContext.IsValidDomainName(name)) return "Validation." + DomainJoinValidationCode.InvalidDomainName;
        string canonical = DomainJoinCredentialContext.CanonicalizeDomainName(name);
        if (current.Domains.Any(domain => !string.Equals(domain.Id, domainId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(domain.DomainName), canonical, StringComparison.Ordinal)))
            return "Validation." + DomainJoinValidationCode.DuplicateDomainName;
        if (domainId is null && current.Domains.Count >= DomainJoinConfigurationValidator.MaximumDomains)
            return "Validation." + DomainJoinValidationCode.TooManyDomains;
        return domainId is not null && current.FindDomain(domainId) is { OrganizationalUnits.Count: > 0 } ? "DomainRenameBlocked" : "DomainInputInvalid";
    }

    /// <summary>Removes the selected domain together with its OUs.</summary>
    [RelayCommand(CanExecute = nameof(CanEditDomain))]
    private void RemoveDomain()
    {
        if (SelectedDomain is { } row) Save(DomainJoinDomainCatalog.Remove(configuration.Current.DomainJoin, row.Id));
    }

    [RelayCommand(CanExecute = nameof(CanSetDefaultDomain))]
    private void SetDefaultDomain()
    {
        if (SelectedDomain is { } row) Save(DomainJoinDomainCatalog.SetDefault(configuration.Current.DomainJoin, row.Id));
    }

    [RelayCommand(CanExecute = nameof(CanEditDomain))]
    private Task AddOrganizationalUnitAsync() => domainDialogs.ShowAddAsync(TryAddOrganizationalUnit);

    /// <summary>Adds one typed OU to the selected domain; returns the reason shown in the dialog when it is refused.</summary>
    private string? TryAddOrganizationalUnit(string displayName, string distinguishedName)
    {
        if (disposed || SelectedDomain is not { } row) return null;
        DomainJoinSettings current = configuration.Current.DomainJoin;
        var unit = new DomainJoinOrganizationalUnitSettings
        { Id = Guid.NewGuid().ToString("D"), DisplayName = displayName.Trim(), DistinguishedName = distinguishedName.Trim() };
        DomainJoinOrganizationalUnitSettings[] added = [unit];
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Merge(current, row.Id, added));
            return null;
        }
        catch (ArgumentException) { return Text(GetMergeFailureKey(row.Settings, added, "OrganizationalUnitInputInvalid")); }
    }

    [RelayCommand(CanExecute = nameof(CanEditOrganizationalUnit))]
    private Task EditOrganizationalUnitAsync()
    {
        if (SelectedDomain is not { } row || selectedOuRows.Count != 1) return Task.CompletedTask;
        DomainJoinOrganizationalUnitSettings unit = selectedOuRows[0].Settings;
        return domainDialogs.ShowRenameAsync(unit, displayName => TryRenameOrganizationalUnit(row.Id, unit.Id, displayName));
    }

    /// <summary>Renames one listed OU; returns the reason shown in the dialog when the name is refused.</summary>
    private string? TryRenameOrganizationalUnit(string domainId, string ouId, string displayName)
    {
        if (disposed) return null;
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Rename(configuration.Current.DomainJoin, domainId, ouId, displayName));
            return null;
        }
        catch (ArgumentException) { return Text("Validation." + DomainJoinValidationCode.InvalidOuDisplayName); }
    }

    /// <summary>Tracks the OU table selection that the OU commands act on.</summary>
    public void ReplaceSelectedOrganizationalUnits(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedOuRows.Clear();
        selectedOuRows.AddRange(rows);
        NotifyOrganizationalUnitCommands();
    }

    /// <summary>Removes the selected OUs of the selected domain, and its default when it is among them.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedOrganizationalUnits))]
    private void RemoveSelectedOrganizationalUnits()
    {
        if (SelectedDomain is not { } row) return;
        DomainJoinSettings settings = configuration.Current.DomainJoin;
        foreach (DomainJoinOrganizationalUnitEntryViewModel unit in selectedOuRows)
        {
            settings = DomainJoinOrganizationalUnitCatalog.Remove(settings, row.Id, unit.Settings.Id);
        }

        Save(settings);
    }

    [RelayCommand(CanExecute = nameof(CanSetDefaultOrganizationalUnit))]
    private void SetDefaultOrganizationalUnit()
    {
        if (SelectedDomain is { } row && selectedOuRows.Count == 1)
            Save(DomainJoinOrganizationalUnitCatalog.SetDefault(configuration.Current.DomainJoin, row.Id, selectedOuRows[0].Settings.Id));
    }

    [RelayCommand(CanExecute = nameof(CanClearDefaultOrganizationalUnit))]
    private void ClearDefaultOrganizationalUnit()
    {
        if (SelectedDomain is { } row) Save(DomainJoinOrganizationalUnitCatalog.SetDefault(configuration.Current.DomainJoin, row.Id, null));
    }

    /// <summary>
    /// Searches the selected domain, then lets the user pick the OUs to add. Invoked again while the search runs,
    /// it cancels that search instead.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ImportFromDomainAsync()
    {
        if (IsDiscovering)
        {
            StopDiscovery();
            SetStatus("DiscoveryCanceled");
            return;
        }

        if (SelectedDomain is not { } row) return;
        string domainId = row.Id;
        DomainJoinSettings baseline = configuration.Current.DomainJoin;
        DomainOuDiscoveryResult? result = await SearchDomainAsync(baseline, row.DomainName);
        if (result is null) return;
        string? failureKey = result.Status switch
        {
            DomainOuDiscoveryStatus.Canceled => "DiscoveryCanceled",
            DomainOuDiscoveryStatus.Unavailable => result.ErrorCode == "Timeout" ? "DiscoveryTimeout" : "DiscoveryUnavailable",
            _ => result.Candidates.Count == 0 ? "DiscoveryEmpty" : null
        };
        // OUs already listed are left out of the picker; selecting them again would add nothing.
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates = failureKey is null
            ? DomainJoinOrganizationalUnitCatalog.ExcludeListed(row.Settings, result.Candidates)
            : [];
        if (failureKey is null && candidates.Count == 0) failureKey = "DiscoveryNothingNew";
        SetStatus(failureKey);
        if (failureKey is not null) return;

        IReadOnlyList<DomainJoinOrganizationalUnitSettings>? selected =
            await domainDialogs.PickAsync(candidates, result.Status == DomainOuDiscoveryStatus.Incomplete);
        // The settings can change while the dialog is open, for example through profile synchronization.
        if (selected is null || selected.Count == 0 || disposed || !ReferenceEquals(baseline, configuration.Current.DomainJoin)) return;
        try { Save(DomainJoinOrganizationalUnitCatalog.Merge(baseline, domainId, selected)); }
        catch (ArgumentException) { SetStatus(GetMergeFailureKey(row.Settings, selected, "ImportDomainMismatch")); }
    }

    /// <summary>Runs one search; returns null when it was canceled, superseded or made stale by a settings change.</summary>
    private async Task<DomainOuDiscoveryResult?> SearchDomainAsync(DomainJoinSettings baseline, string domainName)
    {
        long revision = ++discoveryRevision;
        var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        IsDiscovering = true;
        SetStatus("Discovering");
        try
        {
            DomainOuDiscoveryResult result = await discovery.DiscoverAsync(domainName, cancellation.Token);
            return !disposed && revision == discoveryRevision && ReferenceEquals(baseline, configuration.Current.DomainJoin) ? result : null;
        }
        finally
        {
            if (revision == discoveryRevision)
            {
                IsDiscovering = false;
                discoveryCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>Stops a running search and clears its status, because both describe an earlier state.</summary>
    private void StopDiscovery()
    {
        discoveryRevision++;
        discoveryCancellation?.Cancel();
        discoveryCancellation = null;
        IsDiscovering = false;
        SetStatus(null);
    }

    /// <summary>Names the specific reason a merge was refused, falling back to the caller's general message.</summary>
    private static string GetMergeFailureKey(DomainJoinDomainSettings domain, IReadOnlyList<DomainJoinOrganizationalUnitSettings> added, string fallbackKey)
    {
        if (domain.OrganizationalUnits.Count + added.Count > DomainJoinConfigurationValidator.MaximumOrganizationalUnits)
            return "Validation." + DomainJoinValidationCode.TooManyOrganizationalUnits;
        bool reusesListedId = added.Any(unit => domain.OrganizationalUnits.Any(listed =>
            string.Equals(listed.Id, unit.Id, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(listed.DistinguishedName, unit.DistinguishedName, StringComparison.OrdinalIgnoreCase)));
        return reusesListedId ? "Validation." + DomainJoinValidationCode.DuplicateOuId : fallbackKey;
    }

    partial void OnIsDiscoveringChanged(bool value)
    {
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(DiscoveringVisibility));
    }

    partial void OnSelectedDomainChanged(DomainJoinDomainEntryViewModel? value)
    {
        if (applying) return;
        // A search and its outcome describe the domain that was selected when it started.
        StopDiscovery();
        SynchronizeOrganizationalUnits();
        RefreshPresentation();
    }

    partial void OnSharedAccountNameChanged(string value)
    {
        if (applying) return;
        string trimmed = value.Trim();
        Save(configuration.Current.DomainJoin with { SharedAccountName = trimmed.Length == 0 ? null : trimmed });
    }


    private void Save(DomainJoinSettings settings) => configuration.UpdateDomainJoin(settings);
    private string Text(string key) => localization.GetString("DomainJoin." + key);
    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsDomainListIssue(DomainJoinValidationCode code) =>
        code is DomainJoinValidationCode.DomainsRequired or DomainJoinValidationCode.TooManyDomains or DomainJoinValidationCode.DefaultDomainMissing or
            DomainJoinValidationCode.InvalidDomainId or DomainJoinValidationCode.DuplicateDomainId;

    private static bool IsOrganizationalUnitIssue(DomainJoinValidationCode code) =>
        code is DomainJoinValidationCode.TooManyOrganizationalUnits or DomainJoinValidationCode.InvalidOuId or DomainJoinValidationCode.InvalidOuDisplayName or
            DomainJoinValidationCode.InvalidDistinguishedName or DomainJoinValidationCode.DuplicateOuId or DomainJoinValidationCode.DuplicateDistinguishedName or
            DomainJoinValidationCode.OuOutsideDomain or DomainJoinValidationCode.DefaultOuMissing;

    private string GetIssueText(Func<DomainJoinValidationIssue, bool> ownsIssue)
    {
        foreach (DomainJoinValidationIssue issue in issues)
        {
            if (ownsIssue(issue)) return Text("Validation." + issue.Code);
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns what the shared account card must fix: media protection, or an account problem reported by a domain
    /// that relies on the shared account. Problems of dedicated accounts stay in that domain's row.
    /// </summary>
    private string GetSharedAccountIssueText()
    {
        DomainJoinSettings current = configuration.Current.DomainJoin;
        foreach (DomainJoinValidationIssue issue in issues)
        {
            if (issue.DomainId is null && issue.Code is DomainJoinValidationCode.MediaProtectionRequired or DomainJoinValidationCode.InvalidAccountName)
                return Text("Validation." + issue.Code);
            bool usesSharedAccount = current.FindDomain(issue.DomainId) is { } domain && string.IsNullOrWhiteSpace(domain.AccountName);
            if (usesSharedAccount && issue.Code is DomainJoinValidationCode.SharedAccountRequired or DomainJoinValidationCode.QualifiedAccountRequired or
                DomainJoinValidationCode.PasswordRequired)
                return Text("Validation." + issue.Code);
        }

        return string.Empty;
    }

    private void SetSharedCredentialInputInvalid(bool value)
    {
        sharedCredentialInputInvalid = value;
        OnPropertyChanged(nameof(SharedAccountValidationMessage));
        OnPropertyChanged(nameof(SharedAccountValidationVisibility));
    }

    private void SetStatus(string? key)
    {
        statusKey = key;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusVisibility));
    }

    /// <summary>
    /// Rebuilds the page only when the Domain Join settings themselves changed. Unrelated configuration changes
    /// reuse the same settings instance and must not discard a running search or the table selections.
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(appliedSettings, configuration.Current.DomainJoin))
        {
            RefreshPresentation();
            return;
        }

        StopDiscovery();
        ApplyState();
    }

    private void OnSecretsChanged(object? sender, EventArgs args)
    {
        sharedCredentialInputInvalid = false;
        SecretStateVersion++;
        RefreshPresentation();
    }

    private void OnProtectionSecretsChanged(object? sender, EventArgs args) => RefreshPresentation();

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        foreach (string property in new[]
        {
            nameof(DomainColumnHeader), nameof(AccountColumnHeader), nameof(OuCountColumnHeader), nameof(DefaultColumnHeader), nameof(StatusColumnHeader),
            nameof(LabelColumnHeader), nameof(DistinguishedNameColumnHeader), nameof(EmptyDomainsText), nameof(ImportButtonText), nameof(StatusText),
            nameof(AddLabel), nameof(EditLabel), nameof(RemoveLabel), nameof(SetDefaultLabel), nameof(ClearDefaultLabel)
        })
        {
            OnPropertyChanged(property);
        }

        RefreshPresentation();
    }

    private void ApplyState()
    {
        applying = true;
        try
        {
            DomainJoinSettings settings = configuration.Current.DomainJoin;
            appliedSettings = settings;
            // The password box is refilled below, so an earlier rejected entry no longer describes what is shown.
            sharedCredentialInputInvalid = false;
            SharedAccountName = settings.SharedAccountName ?? string.Empty;
            SynchronizeDomains(settings);
            if (SelectedDomain is null || !Domains.Contains(SelectedDomain))
                SelectedDomain = Domains.FirstOrDefault(row => string.Equals(row.Id, settings.DefaultDomainId, StringComparison.OrdinalIgnoreCase)) ?? Domains.FirstOrDefault();
            SynchronizeOrganizationalUnits();
        }
        finally { applying = false; }
        SecretStateVersion++;
        RefreshPresentation();
    }

    /// <summary>
    /// Aligns the domain rows with the saved domains while keeping the row of every domain that still exists, so
    /// the selection survives a save.
    /// </summary>
    private void SynchronizeDomains(DomainJoinSettings settings)
    {
        IReadOnlyList<DomainJoinDomainSettings> domains = settings.Domains;
        if (Domains.Any(row => settings.FindDomain(row.Id) is null)) DomainRowsRemoving?.Invoke(this, EventArgs.Empty);
        for (int index = Domains.Count - 1; index >= 0; index--)
        {
            if (settings.FindDomain(Domains[index].Id) is null) Domains.RemoveAt(index);
        }

        for (int index = 0; index < domains.Count; index++)
        {
            if (index < Domains.Count && SameId(Domains[index], domains[index])) continue;
            int existing = -1;
            for (int candidate = index + 1; candidate < Domains.Count && existing < 0; candidate++)
            {
                if (SameId(Domains[candidate], domains[index])) existing = candidate;
            }

            if (existing >= 0) Domains.Move(existing, index);
            else Domains.Insert(index, new(domains[index]));
        }

        for (int index = 0; index < domains.Count; index++) Domains[index].Settings = domains[index];

        static bool SameId(DomainJoinDomainEntryViewModel row, DomainJoinDomainSettings domain) =>
            string.Equals(row.Id, domain.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Aligns the OU rows with the selected domain's OUs while keeping the row of every unchanged OU, so the table
    /// selection survives a save.
    /// </summary>
    private void SynchronizeOrganizationalUnits()
    {
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> units = SelectedDomain?.Settings.OrganizationalUnits ?? [];
        if (OrganizationalUnits.Any(row => !units.Contains(row.Settings))) OrganizationalUnitRowsRemoving?.Invoke(this, EventArgs.Empty);
        for (int index = OrganizationalUnits.Count - 1; index >= 0; index--)
        {
            if (!units.Contains(OrganizationalUnits[index].Settings)) OrganizationalUnits.RemoveAt(index);
        }

        for (int index = 0; index < units.Count; index++)
        {
            if (index < OrganizationalUnits.Count && Equals(OrganizationalUnits[index].Settings, units[index])) continue;
            int existing = -1;
            for (int candidate = index + 1; candidate < OrganizationalUnits.Count && existing < 0; candidate++)
            {
                if (Equals(OrganizationalUnits[candidate].Settings, units[index])) existing = candidate;
            }

            if (existing >= 0) OrganizationalUnits.Move(existing, index);
            else OrganizationalUnits.Insert(index, new(units[index]));
        }

        selectedOuRows.RemoveAll(row => !OrganizationalUnits.Contains(row));
    }

    /// <summary>Recomputes readiness issues; an inactive mode reports none because its inputs are disabled.</summary>
    private void RefreshValidation()
    {
        DomainJoinSettings current = configuration.Current.DomainJoin;
        issues = IsActive
            ? DomainJoinConfigurationValidator.EvaluateReadiness(current, secrets.HasPassword,
                configuration.Current.General.DeploymentProtection.IsEnabled && protectionSecrets.IsValid).Issues
            : [];
        string sharedText = Text("SharedAccount");
        string defaultText = Text("DefaultMarker");
        string readyText = Text("DomainReady");
        foreach (DomainJoinDomainEntryViewModel row in Domains)
        {
            DomainJoinValidationIssue? issue = issues.FirstOrDefault(candidate => string.Equals(candidate.DomainId, row.Id, StringComparison.OrdinalIgnoreCase));
            row.AccountText = string.IsNullOrWhiteSpace(row.Settings.AccountName) ? sharedText : row.Settings.AccountName;
            row.DefaultText = string.Equals(current.DefaultDomainId, row.Id, StringComparison.OrdinalIgnoreCase) ? defaultText : string.Empty;
            row.IsReady = issue is null;
            row.StatusText = issue is null ? readyText : Text("Validation." + issue.Code);
        }

        string? defaultOuId = SelectedDomain is { } selected ? GetDefaultOrganizationalUnitId(selected.Settings) : null;
        foreach (DomainJoinOrganizationalUnitEntryViewModel row in OrganizationalUnits)
        {
            row.DefaultText = string.Equals(defaultOuId, row.Settings.Id, StringComparison.OrdinalIgnoreCase) ? defaultText : string.Empty;
        }
    }

    private void RefreshPresentation()
    {
        // Disposal can follow application shutdown, when the shared secret services are already disposed.
        if (disposed) return;
        RefreshValidation();
        foreach (string property in new[]
        {
            nameof(IsActive), nameof(ActionText), nameof(DocumentationUrl), nameof(HasSelectedDomain), nameof(OrganizationalUnitsHeader),
            nameof(EmptyOrganizationalUnitsText), nameof(DomainListVisibility), nameof(EmptyDomainsVisibility), nameof(OrganizationalUnitListVisibility),
            nameof(EmptyOrganizationalUnitsVisibility), nameof(SharedAccountValidationMessage), nameof(SharedAccountValidationVisibility),
            nameof(DomainsValidationMessage), nameof(DomainsValidationVisibility), nameof(OrganizationalUnitsValidationMessage),
            nameof(OrganizationalUnitsValidationVisibility)
        })
        {
            OnPropertyChanged(property);
        }

        EditDomainCommand.NotifyCanExecuteChanged();
        RemoveDomainCommand.NotifyCanExecuteChanged();
        SetDefaultDomainCommand.NotifyCanExecuteChanged();
        AddOrganizationalUnitCommand.NotifyCanExecuteChanged();
        NotifyOrganizationalUnitCommands();
    }

    private static string? GetDefaultOrganizationalUnitId(DomainJoinDomainSettings domain) =>
        DomainJoinOrganizationalUnitCatalog.ResolveDefault(domain.OrganizationalUnits, domain.DefaultOuId)?.Id;

    private void NotifyOrganizationalUnitCommands()
    {
        EditOrganizationalUnitCommand.NotifyCanExecuteChanged();
        RemoveSelectedOrganizationalUnitsCommand.NotifyCanExecuteChanged();
        SetDefaultOrganizationalUnitCommand.NotifyCanExecuteChanged();
        ClearDefaultOrganizationalUnitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Discards search work and subscriptions without clearing credentials owned by the active profile session.</summary>
    public void Dispose()
    {
        disposed = true;
        StopDiscovery();
        configuration.StateChanged -= OnStateChanged;
        secrets.Changed -= OnSecretsChanged;
        protectionSecrets.Changed -= OnProtectionSecretsChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
