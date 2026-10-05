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

/// <summary>Shares domain metadata, volatile credentials and explicit OU preview/import across both authoring pages.</summary>
public sealed partial class DomainJoinConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IDomainJoinSecretStateService secrets;
    private readonly IDeploymentProtectionSecretStateService protectionSecrets;
    private readonly IAuthoringDomainOuDiscoveryService discovery;
    private readonly IDialogService dialogs;
    private readonly IApplicationLocalizationService localization;
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedCatalogRows = [];
    private readonly List<DomainJoinOrganizationalUnitEntryViewModel> selectedPreviewRows = [];
    private IReadOnlyList<DomainJoinValidationCode> issues = [];
    private CancellationTokenSource? discoveryCancellation;
    private FoundryConfigurationDocument? previewBaseline;
    private DomainOuDiscoveryResult? preview;
    private DomainJoinMode pageMode;
    private bool applying;
    private bool disposed;
    private bool credentialInputInvalid;
    private long discoveryRevision;
    private string? catalogStatusKey;
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

    /// <summary>Gets persisted destination rows; discovery does not replace this collection until an explicit import.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> OrganizationalUnits { get; } = [];
    /// <summary>Gets the bounded read-only directory preview; only rows selected in the table are imported.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> PreviewUnits { get; } = [];
    public bool IsActive => configuration.Current.DomainJoin.IsEnabled && configuration.Current.DomainJoin.Mode == pageMode;
    public string ActionText => localization.GetString(IsActive ? "Common.Disable" : "Common.Enable");
    public string DocumentationUrl => pageMode == DomainJoinMode.Interactive
        ? FoundryApplicationInfo.InteractiveDomainJoinDocumentationUrl : FoundryApplicationInfo.ZeroTouchDomainJoinDocumentationUrl;

    public string LabelColumnHeader => localization.GetString("DomainJoinManualLabel.Header");
    public string DistinguishedNameColumnHeader => localization.GetString("DomainJoinManualDn.Header");
    public string RemoveSelectedText => Text("RemoveSelected");
    public string EmptyCatalogText => Text("EmptyCatalog");
    public Visibility CatalogVisibility => ToVisibility(OrganizationalUnits.Count > 0);
    public Visibility EmptyCatalogVisibility => ToVisibility(OrganizationalUnits.Count == 0);

    public string DomainValidationMessage => GetIssueText(IsDomainIssue);
    public Visibility DomainValidationVisibility => ToVisibility(DomainValidationMessage.Length > 0);
    public string CredentialsValidationMessage => credentialInputInvalid ? Text("CredentialInputInvalid") : GetIssueText(IsCredentialIssue);
    public Visibility CredentialsValidationVisibility => ToVisibility(CredentialsValidationMessage.Length > 0);
    public string DestinationValidationMessage => GetIssueText(code => !IsDomainIssue(code) && !IsCredentialIssue(code));
    public Visibility DestinationValidationVisibility => ToVisibility(DestinationValidationMessage.Length > 0);
    public string CatalogStatusText => catalogStatusKey is null ? string.Empty : Text(catalogStatusKey);
    public Visibility CatalogStatusVisibility => ToVisibility(catalogStatusKey is not null);

    public bool CanDiscover => !IsDiscovering && IsActive;
    public bool CanImport => preview is not null && selectedPreviewRows.Count > 0 && ReferenceEquals(previewBaseline, configuration.Current);
    public bool CanRemoveSelected => selectedCatalogRows.Count > 0;
    public string DiscoveryStatusText => discoveryStatusKey is null ? string.Empty : Text(discoveryStatusKey);
    public Visibility DiscoveryStatusVisibility => ToVisibility(discoveryStatusKey is not null);
    public string PreviewDomainText => preview?.ComputerDomain ?? string.Empty;
    public Visibility PreviewVisibility => ToVisibility(PreviewUnits.Count > 0);

    [ObservableProperty]
    public partial string DomainName { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;
    /// <summary>Enables a deployment picker restricted to the authored catalog.</summary>
    [ObservableProperty]
    public partial bool AllowOuSelectionDuringDeployment { get; set; }
    /// <summary>Gets or sets the optional stable catalog default, independent of discovery row identities.</summary>
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
        try
        {
            DomainJoinSettings current = configuration.Current.DomainJoin;
            var unit = new DomainJoinOrganizationalUnitSettings
            { Id = Guid.NewGuid().ToString("D"), DisplayName = ManualDisplayName.Trim(), DistinguishedName = ManualDistinguishedName.Trim() };
            Save(DomainJoinOrganizationalUnitCatalog.Merge(current, current.DomainName ?? string.Empty, (DomainJoinOrganizationalUnitSettings[])[unit]));
            ManualDisplayName = string.Empty;
            ManualDistinguishedName = string.Empty;
            SetCatalogStatus(null);
        }
        catch (ArgumentException) { SetCatalogStatus("CatalogInputInvalid"); }
    }

    /// <summary>Tracks the destination table selection that <see cref="RemoveSelectedCommand"/> acts on.</summary>
    public void ReplaceSelectedCatalogRows(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedCatalogRows.Clear();
        selectedCatalogRows.AddRange(rows);
        OnPropertyChanged(nameof(CanRemoveSelected));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tracks the preview table selection; selection alone never persists a destination.</summary>
    public void ReplaceSelectedPreviewRows(IEnumerable<DomainJoinOrganizationalUnitEntryViewModel> rows)
    {
        selectedPreviewRows.Clear();
        selectedPreviewRows.AddRange(rows);
        OnPropertyChanged(nameof(CanImport));
        ImportSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Removes the selected authored rows and a default among them, retaining unrelated destinations.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        DomainJoinSettings settings = configuration.Current.DomainJoin;
        foreach (DomainJoinOrganizationalUnitEntryViewModel row in selectedCatalogRows)
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
        CancelDiscovery();
        long revision = ++discoveryRevision;
        FoundryConfigurationDocument baseline = configuration.Current;
        var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        IsDiscovering = true;
        SetDiscoveryStatus("Discovering");
        RefreshPresentation();
        try
        {
            DomainOuDiscoveryResult result = await discovery.DiscoverAsync(cancellation.Token);
            if (disposed || revision != discoveryRevision || !ReferenceEquals(baseline, configuration.Current)) return;
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
        if (IsDiscovering) SetDiscoveryStatus("DiscoveryCanceled");
        discoveryRevision++;
        discoveryCancellation?.Cancel();
        discoveryCancellation = null;
        IsDiscovering = false;
        preview = null;
        previewBaseline = null;
        selectedPreviewRows.Clear();
        PreviewUnits.Clear();
        RefreshPresentation();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void ImportSelected()
    {
        if (preview is null || !ReferenceEquals(previewBaseline, configuration.Current)) return;
        DomainJoinOrganizationalUnitSettings[] selected = selectedPreviewRows.Select(row => row.Settings).ToArray();
        try
        {
            Save(DomainJoinOrganizationalUnitCatalog.Merge(configuration.Current.DomainJoin, preview.ComputerDomain ?? string.Empty, selected));
            SetDiscoveryStatus("Imported");
        }
        catch (ArgumentException) { SetDiscoveryStatus("ImportDomainMismatch"); }
    }

    partial void OnIsDiscoveringChanged(bool value) => CancelDiscoveryCommand.NotifyCanExecuteChanged();

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

    private void SetCatalogStatus(string? key)
    {
        catalogStatusKey = key;
        OnPropertyChanged(nameof(CatalogStatusText));
        OnPropertyChanged(nameof(CatalogStatusVisibility));
    }

    private void SetDiscoveryStatus(string? key)
    {
        discoveryStatusKey = key;
        OnPropertyChanged(nameof(DiscoveryStatusText));
        OnPropertyChanged(nameof(DiscoveryStatusVisibility));
    }

    private void OnStateChanged(object? sender, EventArgs args) { CancelDiscovery(); ApplyState(); }
    private void OnSecretsChanged(object? sender, EventArgs args) { SecretStateVersion++; RefreshValidation(); }
    private void OnProtectionSecretsChanged(object? sender, EventArgs args) => RefreshValidation();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        OnPropertyChanged(nameof(LabelColumnHeader));
        OnPropertyChanged(nameof(DistinguishedNameColumnHeader));
        OnPropertyChanged(nameof(RemoveSelectedText));
        OnPropertyChanged(nameof(EmptyCatalogText));
        OnPropertyChanged(nameof(CatalogStatusText));
        OnPropertyChanged(nameof(DiscoveryStatusText));
        RefreshPresentation();
    }

    private void ApplyState()
    {
        applying = true;
        try
        {
            DomainJoinSettings settings = configuration.Current.DomainJoin;
            DomainName = settings.DomainName ?? string.Empty;
            AccountName = settings.AccountName ?? string.Empty;
            AllowOuSelectionDuringDeployment = settings.AllowOuSelectionDuringDeployment;
            selectedCatalogRows.Clear();
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
        var codes = new List<DomainJoinValidationCode>();
        if (IsActive)
        {
            codes.AddRange(DomainJoinConfigurationValidator.EvaluateReadiness(configuration.Current.DomainJoin,
                secrets.HasPassword(Context()), configuration.Current.General.DeploymentProtection.IsEnabled).Issues.Select(issue => issue.Code));
            if (codes.Count == 0 && !configuration.IsDomainJoinConfigurationReady) codes.Add(DomainJoinValidationCode.MediaProtectionRequired);
        }

        issues = codes;
        OnPropertyChanged(nameof(DomainValidationMessage));
        OnPropertyChanged(nameof(DomainValidationVisibility));
        OnPropertyChanged(nameof(CredentialsValidationMessage));
        OnPropertyChanged(nameof(CredentialsValidationVisibility));
        OnPropertyChanged(nameof(DestinationValidationMessage));
        OnPropertyChanged(nameof(DestinationValidationVisibility));
    }

    private void RefreshPresentation()
    {
        RefreshValidation();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(DocumentationUrl));
        OnPropertyChanged(nameof(CatalogVisibility));
        OnPropertyChanged(nameof(EmptyCatalogVisibility));
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(PreviewDomainText));
        OnPropertyChanged(nameof(PreviewVisibility));
        DiscoverCommand.NotifyCanExecuteChanged();
        ImportSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Discards preview work and subscriptions without clearing credentials owned by the active profile session.</summary>
    public void Dispose()
    {
        disposed = true;
        CancelDiscovery();
        configuration.StateChanged -= OnStateChanged;
        secrets.Changed -= OnSecretsChanged;
        protectionSecrets.Changed -= OnProtectionSecretsChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}
