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

/// <summary>Shares domain metadata, volatile credentials and explicit OU search/import across both authoring pages.</summary>
public sealed partial class DomainJoinConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IDomainJoinSecretStateService secrets;
    private readonly IDeploymentProtectionSecretStateService protectionSecrets;
    private readonly IAuthoringDomainOuDiscoveryService discovery;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedListedRows = [];
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedPreviewRows = [];
    private IReadOnlyList<DomainJoinValidationCode> issues = [];
    private CancellationTokenSource? discoveryCancellation;
    private DomainJoinSettings? appliedSettings;
    private DomainJoinSettings? previewBaseline;
    private DomainOuDiscoveryResult? preview;
    private DomainJoinMode pageMode;
    private bool applying;
    private bool disposed;
    private bool credentialInputInvalid;
    private long discoveryRevision;
    private string? listStatusKey;
    private string? discoveryStatusKey;

    public DomainJoinConfigurationViewModel(IFoundryConfigurationStateService configuration,
        IDomainJoinSecretStateService secrets, IDeploymentProtectionSecretStateService protectionSecrets, IAuthoringDomainOuDiscoveryService discovery,
        IDialogService dialogs, IApplicationLocalizationService localization)
    {
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

    /// <summary>Gets the saved OUs; a search does not change this collection until selected results are added.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> OrganizationalUnits { get; } = [];
    /// <summary>Gets the bounded read-only search results; only rows selected in the table are added.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> PreviewUnits { get; } = [];
    public bool IsActive => configuration.Current.DomainJoin.IsEnabled && configuration.Current.DomainJoin.Mode == pageMode;
    public string ActionText => localization.GetString(IsActive ? "Common.Disable" : "Common.Enable");
    public string DocumentationUrl => pageMode == DomainJoinMode.Interactive
        ? FoundryApplicationInfo.InteractiveDomainJoinDocumentationUrl : FoundryApplicationInfo.ZeroTouchDomainJoinDocumentationUrl;

    public string LabelColumnHeader => localization.GetString("DomainJoinManualLabel.Header");
    public string DistinguishedNameColumnHeader => localization.GetString("DomainJoinManualDn.Header");
    public string RemoveSelectedText => Text("RemoveSelected");
    public string EmptyListText => Text("EmptyCatalog");
    /// <summary>Opens the organizational units section on load only when OUs are already listed.</summary>
    public bool HasOrganizationalUnits => OrganizationalUnits.Count > 0;
    public Visibility ListVisibility => ToVisibility(HasOrganizationalUnits);
    public Visibility EmptyListVisibility => ToVisibility(!HasOrganizationalUnits);

    public string DomainValidationMessage => GetIssueText(IsDomainIssue);
    public Visibility DomainValidationVisibility => ToVisibility(DomainValidationMessage.Length > 0);
    public string CredentialsValidationMessage => credentialInputInvalid ? Text("CredentialInputInvalid") : GetIssueText(IsCredentialIssue);
    public Visibility CredentialsValidationVisibility => ToVisibility(CredentialsValidationMessage.Length > 0);
    public string OrganizationalUnitsValidationMessage => GetIssueText(code => !IsDomainIssue(code) && !IsCredentialIssue(code));
    public Visibility OrganizationalUnitsValidationVisibility => ToVisibility(OrganizationalUnitsValidationMessage.Length > 0);
    public string ListStatusText => listStatusKey is null ? string.Empty : Text(listStatusKey);
    public Visibility ListStatusVisibility => ToVisibility(listStatusKey is not null);

    public string DiscoveryStatusText => discoveryStatusKey is null ? string.Empty : Text(discoveryStatusKey);
    public Visibility DiscoveryStatusVisibility => ToVisibility(discoveryStatusKey is not null);
    public string PreviewDomainText => preview?.ComputerDomain ?? string.Empty;
    public Visibility PreviewVisibility => ToVisibility(PreviewUnits.Count > 0);

    private bool CanDiscover => !IsDiscovering && IsActive;
    private bool CanImport => preview is not null && selectedPreviewRows.Count > 0 && ReferenceEquals(previewBaseline, configuration.Current.DomainJoin);
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
    public partial string ManualDisplayName { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ManualDistinguishedName { get; set; } = string.Empty;
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
    private void AddOrganizationalUnit()
    {
        DomainJoinSettings current = configuration.Current.DomainJoin;
        var unit = new DomainJoinOrganizationalUnitSettings
        { Id = Guid.NewGuid().ToString("D"), DisplayName = ManualDisplayName.Trim(), DistinguishedName = ManualDistinguishedName.Trim() };
        DomainJoinOrganizationalUnitSettings[] added = [unit];
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Merge(current, current.DomainName ?? string.Empty, added));
            ManualDisplayName = string.Empty;
            ManualDistinguishedName = string.Empty;
            SetListStatus(null);
        }
        catch (ArgumentException) { SetListStatus(GetMergeFailureKey(current, added, "CatalogInputInvalid")); }
    }

    /// <summary>Tracks the saved-OU table selection that <see cref="RemoveSelectedCommand"/> acts on.</summary>
    public void ReplaceSelectedListedRows(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedListedRows.Clear();
        selectedListedRows.AddRange(rows);
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tracks the search-result selection; selection alone never saves an OU.</summary>
    public void ReplaceSelectedPreviewRows(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedPreviewRows.Clear();
        selectedPreviewRows.AddRange(rows);
        ImportSelectedCommand.NotifyCanExecuteChanged();
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

    [RelayCommand]
    private void ClearDefault() => SelectedDefaultOu = null;

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverAsync()
    {
        DropPreview();
        long revision = ++discoveryRevision;
        DomainJoinSettings baseline = configuration.Current.DomainJoin;
        var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        IsDiscovering = true;
        SetDiscoveryStatus("Discovering");
        RefreshPresentation();
        try
        {
            DomainOuDiscoveryResult result = await discovery.DiscoverAsync(cancellation.Token);
            if (disposed || revision != discoveryRevision || !ReferenceEquals(baseline, configuration.Current.DomainJoin)) return;
            preview = result;
            previewBaseline = baseline;
            foreach (DomainJoinOrganizationalUnitSettings unit in result.Candidates.OrderBy(unit => unit.DistinguishedName, StringComparer.OrdinalIgnoreCase))
                PreviewUnits.Add(new(unit));
            SetDiscoveryStatus(result.Status switch
            {
                DomainOuDiscoveryStatus.Complete => "DiscoveryComplete",
                DomainOuDiscoveryStatus.Incomplete => "DiscoveryIncomplete",
                DomainOuDiscoveryStatus.Canceled => "DiscoveryCanceled",
                _ => result.ErrorCode == "Timeout" ? "DiscoveryTimeout" : "DiscoveryUnavailable"
            });
        }
        finally
        {
            if (revision == discoveryRevision)
            {
                IsDiscovering = false;
                discoveryCancellation = null;
                RefreshPresentation();
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(IsDiscovering))]
    private void CancelDiscovery()
    {
        DropPreview();
        SetDiscoveryStatus("DiscoveryCanceled");
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void ImportSelected()
    {
        if (preview is null || !ReferenceEquals(previewBaseline, configuration.Current.DomainJoin)) return;
        DomainJoinSettings current = configuration.Current.DomainJoin;
        DomainJoinOrganizationalUnitSettings[] selected = selectedPreviewRows.Select(row => row.Settings).ToArray();
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Merge(current, preview.ComputerDomain ?? string.Empty, selected));
            SetDiscoveryStatus("Imported");
        }
        catch (ArgumentException) { SetDiscoveryStatus(GetMergeFailureKey(current, selected, "ImportDomainMismatch")); }
    }

    /// <summary>Stops a running search and discards its results and status, because they describe an earlier state.</summary>
    private void DropPreview()
    {
        discoveryRevision++;
        discoveryCancellation?.Cancel();
        discoveryCancellation = null;
        IsDiscovering = false;
        preview = null;
        previewBaseline = null;
        selectedPreviewRows.Clear();
        PreviewUnits.Clear();
        SetDiscoveryStatus(null);
        RefreshPresentation();
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

    partial void OnIsDiscoveringChanged(bool value) => CancelDiscoveryCommand.NotifyCanExecuteChanged();
    partial void OnManualDisplayNameChanged(string value) => SetListStatus(null);
    partial void OnManualDistinguishedNameChanged(string value) => SetListStatus(null);

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

    private void SetListStatus(string? key)
    {
        listStatusKey = key;
        OnPropertyChanged(nameof(ListStatusText));
        OnPropertyChanged(nameof(ListStatusVisibility));
    }

    private void SetDiscoveryStatus(string? key)
    {
        discoveryStatusKey = key;
        OnPropertyChanged(nameof(DiscoveryStatusText));
        OnPropertyChanged(nameof(DiscoveryStatusVisibility));
    }

    /// <summary>
    /// Rebuilds the page only when the Domain Join settings themselves changed. Unrelated configuration changes
    /// reuse the same settings instance and must not discard a running search, its results or table selections.
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(appliedSettings, configuration.Current.DomainJoin))
        {
            RefreshPresentation();
            return;
        }

        DropPreview();
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
        OnPropertyChanged(nameof(ListStatusText));
        OnPropertyChanged(nameof(DiscoveryStatusText));
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
            selectedListedRows.Clear();
            OrganizationalUnits.Clear();
            foreach (DomainJoinOrganizationalUnitSettings unit in settings.OrganizationalUnits) OrganizationalUnits.Add(new(unit));
            SelectedDefaultOu = OrganizationalUnits.FirstOrDefault(row => string.Equals(row.Settings.Id, settings.DefaultOuId, StringComparison.OrdinalIgnoreCase));
        }
        finally { applying = false; }
        SecretStateVersion++;
        RefreshPresentation();
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
        OnPropertyChanged(nameof(PreviewDomainText));
        OnPropertyChanged(nameof(PreviewVisibility));
        DiscoverCommand.NotifyCanExecuteChanged();
        ImportSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Discards search work and subscriptions without clearing credentials owned by the active profile session.</summary>
    public void Dispose()
    {
        disposed = true;
        DropPreview();
        configuration.StateChanged -= OnStateChanged;
        secrets.Changed -= OnSecretsChanged;
        protectionSecrets.Changed -= OnProtectionSecretsChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
