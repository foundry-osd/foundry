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
    private CancellationTokenSource? discoveryCancellation;
    private FoundryConfigurationDocument? previewBaseline;
    private DomainOuDiscoveryResult? preview;
    private DomainJoinMode pageMode;
    private bool applying;
    private bool disposed;
    private long discoveryRevision;
    private string? statusKey;

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
    /// <summary>Gets the bounded read-only directory preview with explicit per-row import selection.</summary>
    public ObservableCollection<DomainJoinOrganizationalUnitEntryViewModel> PreviewUnits { get; } = [];
    public bool IsActive => configuration.Current.DomainJoin.IsEnabled && configuration.Current.DomainJoin.Mode == pageMode;
    public string ActionText => Text(IsActive ? "Deactivate" : "Activate");
    public string DocumentationUrl => pageMode == DomainJoinMode.Interactive
        ? FoundryApplicationInfo.InteractiveDomainJoinDocumentationUrl : FoundryApplicationInfo.ZeroTouchDomainJoinDocumentationUrl;
    public bool CanDiscover => !IsDiscovering && IsActive;
    public bool CanImport => preview is not null && PreviewUnits.Count > 0 && ReferenceEquals(previewBaseline, configuration.Current);
    public string PreviewDomainText => preview?.ComputerDomain ?? string.Empty;
    public string ReadinessText
    {
        get
        {
            if (!IsActive) return Text("Inactive");
            DomainJoinValidationResult result = DomainJoinConfigurationValidator.EvaluateReadiness(configuration.Current.DomainJoin,
                secrets.HasPassword(Context()), configuration.Current.General.DeploymentProtection.IsEnabled);
            return result.IsValid && configuration.IsDomainJoinConfigurationReady ? Text("Ready")
                : result.Issues.Count > 0 ? Text("Validation." + result.Issues[0].Code) : Text("Validation.MediaProtectionRequired");
        }
    }

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
    public partial string StatusText { get; set; } = string.Empty;
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
        bool deactivate = baseline.DomainJoin.IsEnabled && baseline.DomainJoin.Mode == pageMode;
        bool replace = !deactivate && (baseline.Autopilot.IsEnabled || baseline.DomainJoin.IsEnabled);
        if (replace && !await dialogs.ConfirmAsync(new(Text("ReplacementTitle"), Text("ReplacementMessage"), Text("Activate"), localization.GetString("Common.Cancel"), true))) return;
        if (disposed || !ReferenceEquals(baseline, configuration.Current)) return;
        configuration.UpdateProvisioningSelection(baseline.Autopilot with { IsEnabled = false },
            baseline.DomainJoin with { IsEnabled = !deactivate, Mode = pageMode });
    }

    /// <summary>Copies matching live credentials for PasswordBox synchronization; the caller clears the buffer.</summary>
    public char[]? GetPasswordCopy() => IsActive && pageMode == DomainJoinMode.Automatic ? secrets.GetPasswordCopy(Context()) : null;

    /// <summary>Stores only a context-bound owned password; incomplete identities cannot own a secret.</summary>
    public void SetPassword(ReadOnlySpan<char> password)
    {
        if (!IsActive || pageMode != DomainJoinMode.Automatic) return;
        try { secrets.SetPassword(Context(), password); SetStatus(null); }
        catch (ArgumentException) { secrets.Clear(); SetStatus("CredentialInputInvalid"); }
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
            SetStatus(null);
        }
        catch (ArgumentException) { SetStatus("CatalogInputInvalid"); }
    }

    /// <summary>Removes an authored row and its default, while retaining unrelated destinations.</summary>
    public void RemoveOrganizationalUnit(DomainJoinOrganizationalUnitEntryViewModel row)
    {
        Save(DomainJoinOrganizationalUnitCatalog.Remove(configuration.Current.DomainJoin, row.Settings.Id));
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
        SetStatus("Discovering");
        RefreshPresentation();
        try
        {
            DomainOuDiscoveryResult result = await discovery.DiscoverAsync(cancellation.Token);
            if (disposed || revision != discoveryRevision || !ReferenceEquals(baseline, configuration.Current)) return;
            preview = result;
            previewBaseline = baseline;
            foreach (DomainJoinOrganizationalUnitSettings unit in result.Candidates.OrderBy(unit => unit.DistinguishedName, StringComparer.OrdinalIgnoreCase))
                PreviewUnits.Add(new(unit));
            SetStatus(result.Status switch
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

    [RelayCommand]
    private void CancelDiscovery()
    {
        if (IsDiscovering) SetStatus("DiscoveryCanceled");
        discoveryRevision++;
        discoveryCancellation?.Cancel();
        discoveryCancellation = null;
        IsDiscovering = false;
        preview = null;
        previewBaseline = null;
        PreviewUnits.Clear();
        RefreshPresentation();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void ImportSelected()
    {
        if (preview is null || !ReferenceEquals(previewBaseline, configuration.Current)) return;
        DomainJoinOrganizationalUnitSettings[] selected = PreviewUnits.Where(row => row.IsSelected).Select(row => row.Settings).ToArray();
        if (selected.Length == 0) { SetStatus("SelectDestinations"); return; }
        try { Save(DomainJoinOrganizationalUnitCatalog.Merge(configuration.Current.DomainJoin, preview.ComputerDomain ?? string.Empty, selected)); SetStatus("Imported"); }
        catch (ArgumentException) { SetStatus("ImportDomainMismatch"); }
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
        if (!applying) Save(configuration.Current.DomainJoin with { DefaultOuId = value?.Settings.Id });
    }
    private DomainJoinCredentialContext Context() => new(configuration.Current.DomainJoin.DomainName ?? string.Empty, configuration.Current.DomainJoin.AccountName ?? string.Empty);
    private void Save(DomainJoinSettings settings) => configuration.UpdateDomainJoin(settings);
    private string Text(string key) => localization.GetString("DomainJoin." + key);
    private void SetStatus(string? key) { statusKey = key; StatusText = key is null ? string.Empty : Text(key); }
    private void OnStateChanged(object? sender, EventArgs args) { CancelDiscovery(); ApplyState(); }
    private void OnSecretsChanged(object? sender, EventArgs args) { SecretStateVersion++; OnPropertyChanged(nameof(ReadinessText)); }
    private void OnProtectionSecretsChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(ReadinessText));
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args) { SetStatus(statusKey); RefreshPresentation(); }

    private void ApplyState()
    {
        applying = true;
        try
        {
            DomainJoinSettings settings = configuration.Current.DomainJoin;
            DomainName = settings.DomainName ?? string.Empty;
            AccountName = settings.AccountName ?? string.Empty;
            AllowOuSelectionDuringDeployment = settings.AllowOuSelectionDuringDeployment;
            OrganizationalUnits.Clear();
            foreach (DomainJoinOrganizationalUnitSettings unit in settings.OrganizationalUnits) OrganizationalUnits.Add(new(unit));
            SelectedDefaultOu = OrganizationalUnits.FirstOrDefault(row => string.Equals(row.Settings.Id, settings.DefaultOuId, StringComparison.OrdinalIgnoreCase));
        }
        finally { applying = false; }
        SecretStateVersion++;
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(DocumentationUrl));
        OnPropertyChanged(nameof(ReadinessText));
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(PreviewDomainText));
        DiscoverCommand.NotifyCanExecuteChanged();
        ImportSelectedCommand.NotifyCanExecuteChanged();
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
