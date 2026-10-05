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

/// <summary>Shares domain metadata, volatile credentials and the OU list, with its add and import dialogs, across both authoring pages.</summary>
public sealed partial class DomainJoinConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IDomainJoinSecretStateService secrets;
    private readonly IDeploymentProtectionSecretStateService protectionSecrets;
    private readonly IAuthoringDomainOuDiscoveryService discovery;
    private readonly IDomainJoinOuDialogService ouDialogs;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedListedRows = [];
    private IReadOnlyList<DomainJoinValidationCode> issues = [];
    private CancellationTokenSource? discoveryCancellation;
    private DomainJoinSettings? appliedSettings;
    private DomainJoinMode pageMode;
    private bool applying;
    private bool disposed;
    private bool credentialInputInvalid;
    private long discoveryRevision;
    private string? statusKey;

    public DomainJoinConfigurationViewModel(IFoundryConfigurationStateService configuration,
        IDomainJoinSecretStateService secrets, IDeploymentProtectionSecretStateService protectionSecrets, IAuthoringDomainOuDiscoveryService discovery,
        IDomainJoinOuDialogService ouDialogs, IDialogService dialogs, IApplicationLocalizationService localization)
    {
        this.ouDialogs = ouDialogs;
        this.configuration = configuration;
        this.secrets = secrets;
        this.protectionSecrets = protectionSecrets;
        this.discovery = discovery;
        this.dialogs = dialogs;
        this.localization = localization;
        configuration.StateChanged += OnStateChanged;
        secrets.Changed += OnSecretsChanged;
        protectionSecrets.Changed += OnProtectionSecretsChanged;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyState();
    }

    /// <summary>Gets the saved OUs; a domain search changes this collection only through the import dialog.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> OrganizationalUnits { get; } = [];
    public bool IsActive => configuration.Current.DomainJoin.IsEnabled && configuration.Current.DomainJoin.Mode == pageMode;
    public string ActionText => localization.GetString(IsActive ? "Common.Disable" : "Common.Enable");
    public string DocumentationUrl => pageMode == DomainJoinMode.Interactive
        ? FoundryApplicationInfo.InteractiveDomainJoinDocumentationUrl : FoundryApplicationInfo.ZeroTouchDomainJoinDocumentationUrl;

    public string LabelColumnHeader => localization.GetString("DomainJoinManualLabel.Header");
    public string DistinguishedNameColumnHeader => localization.GetString("DomainJoinManualDn.Header");
    public string RemoveSelectedText => Text("RemoveSelected");
    public string EmptyListText => Text("EmptyCatalog");
    /// <summary>Gets the import button label; the same button cancels a running domain search.</summary>
    public string ImportButtonText => localization.GetString(IsDiscovering ? "DomainJoinCancel.Content" : "DomainJoinDiscover.Content");
    public Visibility ListVisibility => ToVisibility(OrganizationalUnits.Count > 0);
    public Visibility EmptyListVisibility => ToVisibility(OrganizationalUnits.Count == 0);

    public string DomainValidationMessage => GetIssueText(IsDomainIssue);
    public Visibility DomainValidationVisibility => ToVisibility(DomainValidationMessage.Length > 0);
    public string CredentialsValidationMessage => credentialInputInvalid ? Text("CredentialInputInvalid") : GetIssueText(IsCredentialIssue);
    public Visibility CredentialsValidationVisibility => ToVisibility(CredentialsValidationMessage.Length > 0);
    public string OrganizationalUnitsValidationMessage => GetIssueText(code => !IsDomainIssue(code) && !IsCredentialIssue(code));
    public Visibility OrganizationalUnitsValidationVisibility => ToVisibility(OrganizationalUnitsValidationMessage.Length > 0);
    /// <summary>Gets the progress or outcome of the last domain search, shown beside the OU actions.</summary>
    public string StatusText => statusKey is null ? string.Empty : Text(statusKey);
    public Visibility StatusVisibility => ToVisibility(statusKey is not null);
    public Visibility DiscoveringVisibility => ToVisibility(IsDiscovering);

    /// <summary>
    /// Gets or sets whether a default OU applies. Turning it on preselects the first listed OU so the choice is
    /// saved immediately; turning it off returns to the domain's default location.
    /// </summary>
    public bool UseDefaultOu
    {
        get => SelectedDefaultOu is not null;
        set
        {
            if (value != UseDefaultOu) SelectedDefaultOu = value ? OrganizationalUnits.FirstOrDefault() : null;
            // Also reverts the switch when no OU exists to select.
            OnPropertyChanged();
        }
    }
    public bool CanUseDefaultOu => OrganizationalUnits.Count > 0;

    private bool CanRemoveSelected => selectedListedRows.Count > 0;

    [ObservableProperty]
    public partial string DomainName { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;
    /// <summary>Lets the technician pick an OU from the saved list during deployment.</summary>
    [ObservableProperty]
    public partial bool AllowOuSelectionDuringDeployment { get; set; }
    /// <summary>Gets or sets the optional default OU, identified by its stable saved id rather than a search row.</summary>
    [ObservableProperty]
    public partial DomainJoinOrganizationalUnitEntryViewModel? SelectedDefaultOu { get; set; }
    [ObservableProperty]
    public partial bool IsDiscovering { get; set; }
    /// <summary>Invalidates PasswordBox content when volatile secret ownership or profile state changes.</summary>
    [ObservableProperty]
    public partial int SecretStateVersion { get; set; }

    /// <summary>Selects the page presentation without changing the configured provisioning mode.</summary>
    public void SetPageMode(DomainJoinMode mode)
    {
        pageMode = mode;
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

    /// <summary>Copies matching live credentials for PasswordBox synchronization; the caller clears the buffer.</summary>
    public char[]? GetPasswordCopy() => IsActive && pageMode == DomainJoinMode.Automatic ? secrets.GetPasswordCopy(Context()) : null;

    /// <summary>Stores only a context-bound owned password; incomplete identities cannot own a secret.</summary>
    public void SetPassword(ReadOnlySpan<char> password)
    {
        if (!IsActive || pageMode != DomainJoinMode.Automatic) return;
        try
        {
            secrets.SetPassword(Context(), password);
            SetCredentialInputInvalid(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            secrets.Clear();
            SetCredentialInputInvalid(true);
        }
    }

    [RelayCommand]
    private Task AddOrganizationalUnitAsync() => ouDialogs.ShowAddAsync(TryAddOrganizationalUnit);

    /// <summary>Adds one typed OU; returns the reason shown in the dialog when the entry is refused.</summary>
    private string? TryAddOrganizationalUnit(string displayName, string distinguishedName)
    {
        if (disposed) return null;
        DomainJoinSettings current = configuration.Current.DomainJoin;
        var unit = new DomainJoinOrganizationalUnitSettings
        { Id = Guid.NewGuid().ToString("D"), DisplayName = displayName.Trim(), DistinguishedName = distinguishedName.Trim() };
        DomainJoinOrganizationalUnitSettings[] added = [unit];
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Merge(current, current.DomainName ?? string.Empty, added));
            return null;
        }
        catch (ArgumentException) { return Text(GetMergeFailureKey(current, added, "CatalogInputInvalid")); }
    }

    /// <summary>Tracks the saved-OU table selection that <see cref="RemoveSelectedCommand"/> acts on.</summary>
    public void ReplaceSelectedListedRows(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedListedRows.Clear();
        selectedListedRows.AddRange(rows);
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }


    /// <summary>Removes the selected saved OUs and a default among them, keeping the others.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        DomainJoinSettings settings = configuration.Current.DomainJoin;
        foreach (DomainJoinOrganizationalUnitEntryViewModel row in selectedListedRows)
        {
            settings = DomainJoinOrganizationalUnitCatalog.Remove(settings, row.Settings.Id);
        }

        Save(settings);
    }


    /// <summary>
    /// Searches the authoring computer's domain, then lets the user pick the OUs to add. Invoked again while the
    /// search runs, it cancels that search instead.
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

        DomainJoinSettings baseline = configuration.Current.DomainJoin;
        DomainOuDiscoveryResult? result = await SearchDomainAsync(baseline);
        if (result is null) return;
        string? failureKey = result.Status switch
        {
            DomainOuDiscoveryStatus.Canceled => "DiscoveryCanceled",
            DomainOuDiscoveryStatus.Unavailable => result.ErrorCode == "Timeout" ? "DiscoveryTimeout" : "DiscoveryUnavailable",
            _ => result.Candidates.Count == 0 ? "DiscoveryEmpty" : null
        };
        SetStatus(failureKey);
        if (failureKey is not null) return;

        IReadOnlyList<DomainJoinOrganizationalUnitSettings>? selected =
            await ouDialogs.PickAsync(result.Candidates, result.Status == DomainOuDiscoveryStatus.Incomplete);
        // The settings can change while the dialog is open, for example through profile synchronization.
        if (selected is null || selected.Count == 0 || disposed || !ReferenceEquals(baseline, configuration.Current.DomainJoin)) return;
        try { Save(DomainJoinOrganizationalUnitCatalog.Merge(baseline, result.ComputerDomain ?? string.Empty, selected)); }
        catch (ArgumentException) { SetStatus(GetMergeFailureKey(baseline, selected, "ImportDomainMismatch")); }
    }

    /// <summary>Runs one search; returns null when it was canceled, superseded or made stale by a settings change.</summary>
    private async Task<DomainOuDiscoveryResult?> SearchDomainAsync(DomainJoinSettings baseline)
    {
        long revision = ++discoveryRevision;
        var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        IsDiscovering = true;
        SetStatus("Discovering");
        try
        {
            DomainOuDiscoveryResult result = await discovery.DiscoverAsync(cancellation.Token);
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
    private static string GetMergeFailureKey(DomainJoinSettings current, IReadOnlyList<DomainJoinOrganizationalUnitSettings> added, string fallbackKey)
    {
        if (current.OrganizationalUnits.Count + added.Count > DomainJoinConfigurationValidator.MaximumOrganizationalUnits)
            return "Validation." + DomainJoinValidationCode.TooManyOrganizationalUnits;
        bool reusesListedId = added.Any(unit => current.OrganizationalUnits.Any(listed =>
            string.Equals(listed.Id, unit.Id, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(listed.DistinguishedName, unit.DistinguishedName, StringComparison.OrdinalIgnoreCase)));
        return reusesListedId ? "Validation." + DomainJoinValidationCode.DuplicateOuId : fallbackKey;
    }

    partial void OnIsDiscoveringChanged(bool value)
    {
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(DiscoveringVisibility));
    }

    partial void OnDomainNameChanged(string value)
    {
        if (applying) return;
        DomainJoinSettings current = configuration.Current.DomainJoin;
        bool domainChanged = DomainJoinCredentialContext.CanonicalizeDomainName(current.DomainName) != DomainJoinCredentialContext.CanonicalizeDomainName(value);
        Save(current with
        {
            DomainName = value,
            DefaultOuId = domainChanged ? null : current.DefaultOuId,
            AllowOuSelectionDuringDeployment = !domainChanged && current.AllowOuSelectionDuringDeployment
        });
    }
    partial void OnAccountNameChanged(string value) { if (!applying) Save(configuration.Current.DomainJoin with { AccountName = value }); }
    partial void OnAllowOuSelectionDuringDeploymentChanged(bool value)
    {
        if (!applying) Save(configuration.Current.DomainJoin with { AllowOuSelectionDuringDeployment = OrganizationalUnits.Count > 0 && value });
    }
    partial void OnSelectedDefaultOuChanged(DomainJoinOrganizationalUnitEntryViewModel? value)
    {
        OnPropertyChanged(nameof(UseDefaultOu));
        if (!applying) Save(configuration.Current.DomainJoin with { DefaultOuId = value?.Settings.Id });
    }
    private DomainJoinCredentialContext Context() => new(configuration.Current.DomainJoin.DomainName ?? string.Empty, configuration.Current.DomainJoin.AccountName ?? string.Empty);
    private void Save(DomainJoinSettings settings) => configuration.UpdateDomainJoin(settings);
    private string Text(string key) => localization.GetString("DomainJoin." + key);
    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
    private static bool IsDomainIssue(DomainJoinValidationCode code) =>
        code is DomainJoinValidationCode.InvalidDomainName or DomainJoinValidationCode.DomainNameRequired;
    private static bool IsCredentialIssue(DomainJoinValidationCode code) =>
        code is DomainJoinValidationCode.InvalidAccountName or DomainJoinValidationCode.AccountNameRequired or
            DomainJoinValidationCode.QualifiedAccountRequired or DomainJoinValidationCode.PasswordRequired or
            DomainJoinValidationCode.MediaProtectionRequired;

    /// <summary>Returns the first blocking issue owned by one card, so each message sits beside the input that fixes it.</summary>
    private string GetIssueText(Func<DomainJoinValidationCode, bool> ownsIssue)
    {
        foreach (DomainJoinValidationCode code in issues)
        {
            if (ownsIssue(code)) return Text("Validation." + code);
        }

        return string.Empty;
    }

    private void SetCredentialInputInvalid(bool value)
    {
        credentialInputInvalid = value;
        OnPropertyChanged(nameof(CredentialsValidationMessage));
        OnPropertyChanged(nameof(CredentialsValidationVisibility));
    }

    private void SetStatus(string? key)
    {
        statusKey = key;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusVisibility));
    }

    /// <summary>
    /// Rebuilds the page only when the Domain Join settings themselves changed. Unrelated configuration changes
    /// reuse the same settings instance and must not discard a running search or the table selection.
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
        credentialInputInvalid = false;
        SecretStateVersion++;
        RefreshValidation();
    }

    private void OnProtectionSecretsChanged(object? sender, EventArgs args) => RefreshValidation();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        OnPropertyChanged(nameof(LabelColumnHeader));
        OnPropertyChanged(nameof(DistinguishedNameColumnHeader));
        OnPropertyChanged(nameof(RemoveSelectedText));
        OnPropertyChanged(nameof(EmptyListText));
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(StatusText));
        RefreshPresentation();
    }

    private void ApplyState()
    {
        applying = true;
        try
        {
            DomainJoinSettings settings = configuration.Current.DomainJoin;
            appliedSettings = settings;
            // The password box is emptied below, so an earlier rejected entry no longer describes what is shown.
            credentialInputInvalid = false;
            DomainName = settings.DomainName ?? string.Empty;
            AccountName = settings.AccountName ?? string.Empty;
            AllowOuSelectionDuringDeployment = settings.AllowOuSelectionDuringDeployment;
            SynchronizeOrganizationalUnits(settings.OrganizationalUnits);
            selectedListedRows.RemoveAll(row => !OrganizationalUnits.Contains(row));
            SelectedDefaultOu = OrganizationalUnits.FirstOrDefault(row => string.Equals(row.Settings.Id, settings.DefaultOuId, StringComparison.OrdinalIgnoreCase));
        }
        finally { applying = false; }
        SecretStateVersion++;
        RefreshPresentation();
    }

    /// <summary>
    /// Aligns the rows with the saved OUs while keeping the row of every unchanged OU. Rebuilding the collection
    /// would reset the default OU box, which then shows nothing although a default is still selected.
    /// </summary>
    private void SynchronizeOrganizationalUnits(IReadOnlyList<DomainJoinOrganizationalUnitSettings> units)
    {
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
    }

    /// <summary>Recomputes readiness issues; an inactive mode reports none because its inputs are disabled.</summary>
    private void RefreshValidation()
    {
        issues = IsActive
            ? DomainJoinConfigurationValidator.EvaluateReadiness(configuration.Current.DomainJoin, secrets.HasPassword(Context()),
                configuration.Current.General.DeploymentProtection.IsEnabled && protectionSecrets.IsValid).Issues.Select(issue => issue.Code).ToArray()
            : [];
        OnPropertyChanged(nameof(DomainValidationMessage));
        OnPropertyChanged(nameof(DomainValidationVisibility));
        OnPropertyChanged(nameof(CredentialsValidationMessage));
        OnPropertyChanged(nameof(CredentialsValidationVisibility));
        OnPropertyChanged(nameof(OrganizationalUnitsValidationMessage));
        OnPropertyChanged(nameof(OrganizationalUnitsValidationVisibility));
    }

    private void RefreshPresentation()
    {
        // Disposal can follow application shutdown, when the shared secret services are already disposed.
        if (disposed) return;
        RefreshValidation();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(DocumentationUrl));
        OnPropertyChanged(nameof(ListVisibility));
        OnPropertyChanged(nameof(EmptyListVisibility));
        OnPropertyChanged(nameof(CanUseDefaultOu));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
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
