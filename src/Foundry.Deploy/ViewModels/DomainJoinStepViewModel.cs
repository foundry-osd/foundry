// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.ViewModels;

/// <summary>Says what an entry of the domain list stands for.</summary>
public enum DomainJoinChoiceKind { ListedDomain, TypedDomain, DoNotJoin }

/// <summary>
/// One entry of the domain list on the Domain Join step: a domain the media lists, the join of a domain the
/// technician types, or no join at all.
/// </summary>
public sealed record DomainJoinChoice(DomainJoinChoiceKind Kind, string DisplayName, DeployDomainJoinDomainSettings? Domain = null);

/// <summary>Where the check of the typed account and password with the domain stands for the current inputs.</summary>
public enum DomainCredentialCheckState
{
    /// <summary>The current inputs have not been checked.</summary>
    NotChecked,

    /// <summary>A check of the current inputs is running.</summary>
    Checking,

    /// <summary>The domain accepted the current account and password.</summary>
    Valid,

    /// <summary>The domain rejected the current account or password; the step stays blocked until an input changes.</summary>
    Rejected,

    /// <summary>The current inputs could not be checked; the next confirmation continues without a new check.</summary>
    Warning,

    /// <summary>The technician continued after a warning for the current inputs.</summary>
    WarningAcknowledged
}

/// <summary>
/// Holds what the technician enters on the Domain Join wizard step: the domain to join or the choice not to join,
/// credentials in Interactive mode, and the OU when the retained domain lists several. The password is an owned
/// buffer that never enters wizard state, logs or the deployment request.
/// </summary>
public sealed partial class DomainJoinStepViewModel : ObservableObject, IDisposable
{
    private DeployDomainJoinSettings settings = new();
    private IReadOnlyList<DomainJoinChoice> domainChoices = [];
    private char[]? password;
    private int credentialCheckGeneration;

    /// <summary>Raised when an input or its validity changes, so the wizard can refresh navigation.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when the owned password is erased, so the view can empty its password box.</summary>
    public event EventHandler? PasswordCleared;

    /// <summary>Listed domain shown in the domain list; the default is preselected.</summary>
    [ObservableProperty]
    private DeployDomainJoinDomainSettings? selectedDomain;

    /// <summary>
    /// Whether the technician chose not to join a domain for this deployment although the media configures the
    /// join. It is never the initial state: the media default stays the preselected choice.
    /// </summary>
    [ObservableProperty]
    private bool isJoinDeclined;

    /// <summary>Name of the retained domain, or the name typed when the media lists no domain.</summary>
    [ObservableProperty]
    private string domainName = string.Empty;

    [ObservableProperty]
    private string accountName = string.Empty;

    [ObservableProperty]
    private DomainJoinOrganizationalUnitSettings? selectedOrganizationalUnit;

    [ObservableProperty]
    private string typedOuDistinguishedName = string.Empty;

    /// <summary>Where the check of the current account and password stands; any change of domain, account or password resets it.</summary>
    [ObservableProperty]
    private DomainCredentialCheckState credentialCheckState;

    /// <summary>Gets the result of the last check applied to the current inputs, or <see langword="null"/> when they were not checked.</summary>
    public DomainCredentialCheckResult? LastCredentialCheck { get; private set; }

    public IReadOnlyList<DeployDomainJoinDomainSettings> Domains => settings.Domains;

    /// <summary>
    /// Gets the entries of the domain list: the listed domains in media order, or one entry for a typed domain when
    /// the media lists none, then the choice not to join, always last.
    /// </summary>
    public IReadOnlyList<DomainJoinChoice> DomainChoices => domainChoices;

    /// <summary>Gets or sets the entry the domain list shows as selected; it mirrors the retained domain and the declined state.</summary>
    public DomainJoinChoice? SelectedDomainChoice
    {
        get => domainChoices.FirstOrDefault(choice => choice.Kind switch
        {
            DomainJoinChoiceKind.DoNotJoin => IsJoinDeclined,
            DomainJoinChoiceKind.TypedDomain => !IsJoinDeclined,
            _ => !IsJoinDeclined && string.Equals(choice.Domain?.Id, SelectedDomain?.Id, StringComparison.OrdinalIgnoreCase)
        });
        set
        {
            // The list control reports no selection while its entries are replaced; that is not a choice.
            if (value is null) return;
            if (value.Domain is not null) SelectedDomain = value.Domain;
            IsJoinDeclined = value.Kind == DomainJoinChoiceKind.DoNotJoin;
        }
    }

    /// <summary>Gets the OUs of the retained domain; they change with the domain.</summary>
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits => RetainedDomain?.OrganizationalUnits ?? [];

    /// <summary>Gets whether the technician supplies the account and password.</summary>
    public bool RequiresCredentials => settings.IsEnabled && settings.Mode == DomainJoinMode.Interactive && !IsJoinDeclined;

    /// <summary>Gets whether the domain list is shown, which is the case whenever the media configures the join.</summary>
    public bool IsDomainListVisible => settings.IsEnabled;

    /// <summary>Gets whether the domain is typed: the media lists none and the technician joins one.</summary>
    public bool IsDomainTextVisible => settings.IsEnabled && !HasListedDomains && !IsJoinDeclined;

    /// <summary>Gets whether the technician picks an OU: the retained domain lists more than one. A single listed OU is used as is.</summary>
    public bool IsOuListVisible => !IsJoinDeclined && OrganizationalUnits.Count > 1;

    /// <summary>Gets whether an OU may be typed, which is only offered in Interactive mode for a domain without listed OUs.</summary>
    public bool IsTypedOuVisible => RequiresCredentials && OrganizationalUnits.Count == 0;

    /// <summary>
    /// Gets whether the step has anything for the technician to enter or choose. A media that configures the join
    /// always has: at the least, the choice not to join.
    /// </summary>
    public bool HasInput => settings.IsEnabled;

    public bool HasPassword => password is { Length: > 0 };

    public bool IsDomainInvalid => IsDomainTextVisible && DomainName.Length > 0 && !DomainJoinCredentialContext.IsValidDomainName(DomainName);

    public bool IsAccountInvalid => AccountName.Length > 0 && !DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName);

    public bool IsTypedOuInvalid => IsTypedOuVisible && TypedOu.Length > 0 &&
        !(DistinguishedNameRules.IsOrganizationalUnit(TypedOu) && DistinguishedNameRules.IsWithinDomain(TypedOu, DomainName));

    /// <summary>Gets whether the wizard may leave the step and start a deployment with these inputs.</summary>
    public bool IsValid => !HasInput || IsJoinDeclined ||
        (HasListedDomains ? RetainedDomain is not null : DomainJoinCredentialContext.IsValidDomainName(DomainName)) &&
        (!RequiresCredentials || DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName) && HasPassword) &&
        (!IsOuListVisible || SelectedOrganizationalUnit is not null) &&
        !IsTypedOuInvalid &&
        CredentialCheckState is not (DomainCredentialCheckState.Checking or DomainCredentialCheckState.Rejected);

    /// <summary>Gets whether leaving the step would contact the domain or acknowledge a warning for the current inputs.</summary>
    public bool NeedsCredentialCheck => RequiresCredentials &&
        CredentialCheckState is DomainCredentialCheckState.NotChecked or DomainCredentialCheckState.Warning;

    public bool IsCheckingCredentials => CredentialCheckState == DomainCredentialCheckState.Checking;

    public bool HasCredentialCheckError => CredentialCheckState == DomainCredentialCheckState.Rejected;

    public bool HasCredentialCheckWarning => CredentialCheckState is DomainCredentialCheckState.Warning or DomainCredentialCheckState.WarningAcknowledged;

    /// <summary>Gets the localized rejection or warning shown under the password, or an empty string.</summary>
    public string CredentialCheckMessage => (CredentialCheckState, LastCredentialCheck?.Outcome) switch
    {
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.AccountLocked) => LocalizationText.GetString("DomainJoin.AccountLocked"),
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.PasswordExpired) => LocalizationText.GetString("DomainJoin.PasswordExpired"),
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.AccountDisabled) => LocalizationText.GetString("DomainJoin.AccountDisabled"),
        (DomainCredentialCheckState.Rejected, _) => LocalizationText.GetString("DomainJoin.CredentialsRejected"),
        (DomainCredentialCheckState.Warning or DomainCredentialCheckState.WarningAcknowledged, DomainCredentialCheckOutcome.ControllerUnavailable) =>
            LocalizationText.GetString("DomainJoin.DomainUnreachable"),
        (DomainCredentialCheckState.Warning or DomainCredentialCheckState.WarningAcknowledged, _) => LocalizationText.GetString("DomainJoin.CheckInconclusive"),
        _ => string.Empty
    };

    /// <summary>Gets the OU the join will use; <see langword="null"/> means the domain's default location.</summary>
    public string? EffectiveOuDistinguishedName =>
        IsJoinDeclined ? null :
        IsOuListVisible ? SelectedOrganizationalUnit?.DistinguishedName :
        IsTypedOuVisible ? (TypedOu.Length > 0 && !IsTypedOuInvalid ? TypedOu : null) :
        DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(RetainedDomain)?.DistinguishedName;

    private bool HasListedDomains => settings.Domains.Count > 0;

    /// <summary>The domain the join will use: the technician's choice among several, otherwise the media default.</summary>
    private DeployDomainJoinDomainSettings? RetainedDomain => DomainJoinPreparationService.ResolveDomain(settings, SelectedDomain?.Id);

    private string TypedOu => TypedOuDistinguishedName.Trim();

    /// <summary>Loads the media configuration and discards every earlier input, including the password.</summary>
    public void Configure(DeployDomainJoinSettings domainJoin)
    {
        settings = domainJoin ?? throw new ArgumentNullException(nameof(domainJoin));
        ClearPassword();
        ResetCredentialCheck();
        // A typed domain belongs to the earlier configuration; a listed one is shown again below.
        DomainName = string.Empty;
        AccountName = string.Empty;
        TypedOuDistinguishedName = string.Empty;
        // Declining belongs to the earlier configuration; a new one starts on its default domain.
        IsJoinDeclined = false;
        OnPropertyChanged(nameof(Domains));
        SelectedDomain = DomainJoinPreparationService.ResolveDomain(settings, null);
        RebuildDomainChoices();
        ApplyRetainedDomain();
    }

    /// <summary>Rebuilds the entries of the domain list after a language change, keeping the technician's choice.</summary>
    public void RefreshLocalizedText()
    {
        RebuildDomainChoices();
        OnPropertyChanged(nameof(CredentialCheckMessage));
    }

    /// <summary>
    /// Decides whether the wizard may leave the step. Without credentials to check it returns <see langword="true"/> at once.
    /// Otherwise the first call checks the current inputs with the domain: a valid account advances, a rejection blocks, and a
    /// failure to check returns <see langword="false"/> once as a warning that the next call acknowledges without a new check.
    /// A result is discarded when an input changed while the check ran.
    /// </summary>
    public async Task<bool> ConfirmCredentialsAsync(IDomainCredentialVerifier verifier, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        if (!RequiresCredentials) return true;
        switch (CredentialCheckState)
        {
            case DomainCredentialCheckState.Valid or DomainCredentialCheckState.WarningAcknowledged:
                return true;
            case DomainCredentialCheckState.Warning:
                CredentialCheckState = DomainCredentialCheckState.WarningAcknowledged;
                return true;
            case DomainCredentialCheckState.Checking or DomainCredentialCheckState.Rejected:
                return false;
        }

        int generation = credentialCheckGeneration;
        var context = new DomainJoinCredentialContext(DomainName, AccountName);
        char[] secret = password?.ToArray() ?? [];
        CredentialCheckState = DomainCredentialCheckState.Checking;
        DomainCredentialCheckResult result;
        try
        {
            result = await verifier.VerifyAsync(context, secret, token);
        }
        catch (OperationCanceledException)
        {
            if (generation == credentialCheckGeneration) CredentialCheckState = DomainCredentialCheckState.NotChecked;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(secret.AsSpan()));
        }

        if (generation != credentialCheckGeneration) return false;
        LastCredentialCheck = result;
        CredentialCheckState = result.Outcome == DomainCredentialCheckOutcome.Valid ? DomainCredentialCheckState.Valid :
            result.IsRejection ? DomainCredentialCheckState.Rejected : DomainCredentialCheckState.Warning;
        return CredentialCheckState == DomainCredentialCheckState.Valid;
    }

    /// <summary>Replaces the owned password with a copy of the supplied characters.</summary>
    public void SetPassword(ReadOnlySpan<char> value)
    {
        ErasePassword();
        password = value.IsEmpty ? null : value.ToArray();
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
    }

    /// <summary>Returns an independent copy for refilling the password box; the caller clears it.</summary>
    public char[]? GetPasswordCopy() => password?.ToArray();

    /// <summary>Erases the owned password and tells the view to empty its password box.</summary>
    public void ClearPassword()
    {
        if (password is null) return;
        ErasePassword();
        PasswordCleared?.Invoke(this, EventArgs.Empty);
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
    }

    /// <summary>Copies the current inputs for launch preparation; the caller disposes the result.</summary>
    public DomainJoinSubmission? CreateSubmission() => !settings.IsEnabled ? null : IsJoinDeclined ? DomainJoinSubmission.Declined() : new(
        HasListedDomains ? RetainedDomain?.Id : null,
        DomainName,
        RequiresCredentials ? AccountName : string.Empty,
        IsOuListVisible ? SelectedOrganizationalUnit?.Id : null,
        RequiresCredentials ? password : null,
        IsTypedOuVisible && TypedOu.Length > 0 ? TypedOu : null);

    public void Dispose() => ErasePassword();

    partial void OnSelectedDomainChanged(DeployDomainJoinDomainSettings? value)
    {
        ResetCredentialCheck();
        ApplyRetainedDomain();
    }

    partial void OnIsJoinDeclinedChanged(bool value)
    {
        // A password typed before declining is not needed any more and must not outlive the choice.
        if (value) ClearPassword();
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
    }

    partial void OnDomainNameChanged(string value)
    {
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
    }

    /// <summary>
    /// Shows the retained domain and resets the OU to the one that domain uses or preselects. The account and password are kept,
    /// because one account may serve several domains.
    /// </summary>
    private void ApplyRetainedDomain()
    {
        DeployDomainJoinDomainSettings? retained = RetainedDomain;
        if (HasListedDomains) DomainName = retained?.DomainName ?? string.Empty;
        TypedOuDistinguishedName = string.Empty;
        OnPropertyChanged(nameof(OrganizationalUnits));
        SelectedOrganizationalUnit = DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(retained);
        RaiseDerivedStateChanged();
    }

    partial void OnAccountNameChanged(string value)
    {
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
    }

    partial void OnCredentialCheckStateChanged(DomainCredentialCheckState value)
    {
        OnPropertyChanged(nameof(NeedsCredentialCheck));
        OnPropertyChanged(nameof(IsCheckingCredentials));
        OnPropertyChanged(nameof(HasCredentialCheckError));
        OnPropertyChanged(nameof(HasCredentialCheckWarning));
        OnPropertyChanged(nameof(CredentialCheckMessage));
        OnPropertyChanged(nameof(IsValid));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnSelectedOrganizationalUnitChanged(DomainJoinOrganizationalUnitSettings? value) => RaiseDerivedStateChanged();
    partial void OnTypedOuDistinguishedNameChanged(string value) => RaiseDerivedStateChanged();

    private void RebuildDomainChoices()
    {
        var choices = new List<DomainJoinChoice>();
        if (settings.IsEnabled)
        {
            if (HasListedDomains)
                choices.AddRange(settings.Domains.Select(domain => new DomainJoinChoice(DomainJoinChoiceKind.ListedDomain, domain.DomainName, domain)));
            else
                choices.Add(new(DomainJoinChoiceKind.TypedDomain, LocalizationText.GetString("DomainJoin.JoinDomain")));
            // Not joining is the technician's own decision, so it comes last and is never preselected.
            choices.Add(new(DomainJoinChoiceKind.DoNotJoin, LocalizationText.GetString("DomainJoin.DoNotJoin")));
        }

        domainChoices = choices;
        OnPropertyChanged(nameof(DomainChoices));
        OnPropertyChanged(nameof(SelectedDomainChoice));
    }

    /// <summary>Invalidates any check of earlier inputs, including one still running, so its result is never applied.</summary>
    private void ResetCredentialCheck()
    {
        credentialCheckGeneration++;
        LastCredentialCheck = null;
        CredentialCheckState = DomainCredentialCheckState.NotChecked;
    }

    private void ErasePassword()
    {
        if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        password = null;
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(RequiresCredentials));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(SelectedDomainChoice));
        OnPropertyChanged(nameof(IsDomainListVisible));
        OnPropertyChanged(nameof(IsDomainTextVisible));
        OnPropertyChanged(nameof(IsOuListVisible));
        OnPropertyChanged(nameof(IsTypedOuVisible));
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(IsDomainInvalid));
        OnPropertyChanged(nameof(IsAccountInvalid));
        OnPropertyChanged(nameof(IsTypedOuInvalid));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(NeedsCredentialCheck));
        OnPropertyChanged(nameof(EffectiveOuDistinguishedName));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
