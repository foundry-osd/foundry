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
using Foundry.Wpf.Controls;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

/// <summary>Says what an entry of the domain list stands for.</summary>
public enum DomainJoinChoiceKind { ListedDomain, TypedDomain, DoNotJoin }

/// <summary>
/// One entry of the domain list on the Domain Join step: a domain the media lists, the join of a domain the
/// technician types, or no join at all.
/// </summary>
public sealed record DomainJoinChoice(DomainJoinChoiceKind Kind, string DisplayName, DeployDomainJoinDomainSettings? Domain = null);

/// <summary>
/// Where the check of the join account with the domain stands for the current inputs: the account and password typed on
/// Interactive media, or the account stored on Zero-touch media for the chosen domain.
/// </summary>
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

    /// <summary>The current Interactive inputs could not be checked; the next confirmation continues without a new check.</summary>
    Warning,

    /// <summary>
    /// The current inputs could not be checked and the step may be left: the technician confirmed the warning on Interactive
    /// media, and Zero-touch media never asks, since nothing on the step can correct the stored account.
    /// </summary>
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
    private ZeroTouchCheckSession? zeroTouchCheck;
    private int skippedZeroTouchGeneration = -1;
    // The stored account cannot change during a session, so a domain is asked once: asking again with a refused password
    // would only count another failed sign-in against the shared join account.
    private readonly Dictionary<string, DomainCredentialCheckResult> zeroTouchAnswers = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? zeroTouchCancellation;

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

    /// <summary>
    /// Gets whether leaving the step would check the typed Interactive credentials or acknowledge a warning about them.
    /// Zero-touch media checks on its own while the step is shown.
    /// </summary>
    public bool NeedsCredentialCheck => RequiresCredentials &&
        CredentialCheckState is DomainCredentialCheckState.NotChecked or DomainCredentialCheckState.Warning;

    public bool IsCheckingCredentials => CredentialCheckState == DomainCredentialCheckState.Checking;

    /// <summary>
    /// Gets the severity of the card that reports the check at the bottom of the step: in progress, accepted, warning or refused.
    /// <see langword="null"/> means the current inputs have not been checked and no card is shown.
    /// </summary>
    public InfoBarSeverity? CredentialCheckSeverity => CredentialCheckState switch
    {
        DomainCredentialCheckState.Checking => InfoBarSeverity.Informational,
        DomainCredentialCheckState.Valid => InfoBarSeverity.Success,
        DomainCredentialCheckState.Rejected => InfoBarSeverity.Error,
        DomainCredentialCheckState.Warning or DomainCredentialCheckState.WarningAcknowledged => InfoBarSeverity.Warning,
        _ => null
    };

    /// <summary>Gets whether the check card is shown.</summary>
    public bool IsCredentialCheckShown => CredentialCheckSeverity is not null;

    /// <summary>Gets the localized text of that card, or an empty string when no card is shown.</summary>
    public string CredentialCheckMessage => CredentialCheckState switch
    {
        DomainCredentialCheckState.Checking => LocalizationText.GetString("DomainJoin.CheckingCredentials"),
        DomainCredentialCheckState.Valid => LocalizationText.GetString("DomainJoin.CredentialsVerified"),
        _ => settings.Mode == DomainJoinMode.Automatic ? ZeroTouchCredentialCheckMessage : InteractiveCredentialCheckMessage
    };

    private string InteractiveCredentialCheckMessage => (CredentialCheckState, LastCredentialCheck?.Outcome) switch
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

    private string ZeroTouchCredentialCheckMessage => (CredentialCheckState, LastCredentialCheck?.Outcome) switch
    {
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.AccountLocked) => LocalizationText.GetString("DomainJoin.ZeroTouchAccountLocked"),
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.PasswordExpired) => LocalizationText.GetString("DomainJoin.ZeroTouchPasswordExpired"),
        (DomainCredentialCheckState.Rejected, DomainCredentialCheckOutcome.AccountDisabled) => LocalizationText.GetString("DomainJoin.ZeroTouchAccountDisabled"),
        (DomainCredentialCheckState.Rejected, _) => LocalizationText.GetString("DomainJoin.ZeroTouchCredentialsRejected"),
        (DomainCredentialCheckState.WarningAcknowledged, DomainCredentialCheckOutcome.ControllerUnavailable) =>
            LocalizationText.GetString("DomainJoin.ZeroTouchDomainUnreachable"),
        (DomainCredentialCheckState.WarningAcknowledged, _) => LocalizationText.GetString("DomainJoin.ZeroTouchCheckInconclusive"),
        _ => string.Empty
    };

    /// <summary>Gets whether the Zero-touch account stored on the media applies to the current choice and can be checked.</summary>
    private bool IsZeroTouchCheckApplicable => settings.IsEnabled && settings.Mode == DomainJoinMode.Automatic && !IsJoinDeclined && RetainedDomain is not null;

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
        zeroTouchAnswers.Clear();
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
        StartZeroTouchCheck();
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
        catch (Exception)
        {
            // A failure that is not a directory answer about the account must warn, never block or stop Deploy.
            result = new(DomainCredentialCheckOutcome.Inconclusive);
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
        StartZeroTouchCheck();
    }

    /// <summary>
    /// Starts checking the join account stored on Zero-touch media while the step is shown: now, and again whenever another domain
    /// is chosen. A refusal blocks the step until the technician declines the join; a domain that cannot be reached, or an unclear
    /// answer, only shows a warning, since nothing on the step can correct it. Interactive media never uses this check.
    /// </summary>
    public void BeginZeroTouchCheck(IZeroTouchMediaCredentialCheck mediaCheck, IDomainCredentialVerifier verifier, ILogger logger)
    {
        // Navigation refreshes call this repeatedly while the step is shown; only a new pair of dependencies starts a check.
        if (zeroTouchCheck is { } current && ReferenceEquals(current.MediaCheck, mediaCheck) && ReferenceEquals(current.Verifier, verifier)) return;
        zeroTouchCheck = new(mediaCheck ?? throw new ArgumentNullException(nameof(mediaCheck)),
            verifier ?? throw new ArgumentNullException(nameof(verifier)), logger ?? throw new ArgumentNullException(nameof(logger)));
        StartZeroTouchCheck();
    }

    /// <summary>Stops starting Zero-touch checks once the step is left; a check already running still applies its result.</summary>
    public void EndZeroTouchCheck() => zeroTouchCheck = null;

    private void StartZeroTouchCheck()
    {
        if (zeroTouchCheck is { } session && IsZeroTouchCheckApplicable && CredentialCheckState == DomainCredentialCheckState.NotChecked &&
            skippedZeroTouchGeneration != credentialCheckGeneration)
        {
            _ = RunZeroTouchCheckAsync(session);
        }
    }

    private async Task RunZeroTouchCheckAsync(ZeroTouchCheckSession session)
    {
        try
        {
            await RunZeroTouchCheckCoreAsync(session);
        }
        catch (Exception exception)
        {
            // Started without an awaiting caller; a failure here must not become an unobserved task exception.
            session.Logger.LogError(exception, "Zero-touch domain credential check failed unexpectedly.");
        }
    }

    private async Task RunZeroTouchCheckCoreAsync(ZeroTouchCheckSession session)
    {
        int generation = credentialCheckGeneration;
        string domainId = RetainedDomain!.Id;
        if (zeroTouchAnswers.TryGetValue(domainId, out DomainCredentialCheckResult? known))
        {
            ApplyZeroTouchAnswer(known);
            return;
        }

        zeroTouchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        zeroTouchCancellation = cancellation;
        CredentialCheckState = DomainCredentialCheckState.Checking;
        session.Logger.LogInformation("Zero-touch domain credential check started.");
        DomainCredentialCheckResult? result;
        try
        {
            result = await session.MediaCheck.VerifyMediaCredentialsAsync(settings, domainId, session.Verifier, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            result = null;
            generation = -1;
        }
        catch (Exception)
        {
            // A failure that is not a directory answer about the account must warn, never block or stop Deploy.
            result = new(DomainCredentialCheckOutcome.Inconclusive);
        }
        finally
        {
            if (ReferenceEquals(zeroTouchCancellation, cancellation)) zeroTouchCancellation = null;
        }

        if (generation != credentialCheckGeneration)
        {
            session.Logger.LogInformation("Zero-touch domain credential check discarded because the domain changed.");
            return;
        }

        if (result is null)
        {
            session.Logger.LogInformation("Zero-touch domain credential check skipped because the media credentials are not available.");
            // Not retried for the same inputs; the launch preparation reports locked or unusable media itself.
            skippedZeroTouchGeneration = generation;
            CredentialCheckState = DomainCredentialCheckState.NotChecked;
            return;
        }

        session.Logger.Log(
            result.Outcome == DomainCredentialCheckOutcome.Valid ? LogLevel.Information : result.IsRejection ? LogLevel.Error : LogLevel.Warning,
            "Zero-touch domain credential check completed with {Outcome} (LDAP {LdapErrorCode}, data {DirectoryDataCode:x}, native {NativeErrorCode}).",
            result.Outcome,
            result.LdapErrorCode,
            result.DirectoryDataCode,
            result.NativeErrorCode);
        // Only directory answers about the account are kept; an unreachable domain may answer later.
        if (result.Outcome == DomainCredentialCheckOutcome.Valid || result.IsRejection) zeroTouchAnswers[domainId] = result;
        ApplyZeroTouchAnswer(result);
    }

    private void ApplyZeroTouchAnswer(DomainCredentialCheckResult result)
    {
        LastCredentialCheck = result;
        CredentialCheckState = result.Outcome == DomainCredentialCheckOutcome.Valid ? DomainCredentialCheckState.Valid :
            result.IsRejection ? DomainCredentialCheckState.Rejected : DomainCredentialCheckState.WarningAcknowledged;
    }

    /// <summary>Dependencies of the Zero-touch check while the step is shown.</summary>
    private sealed record ZeroTouchCheckSession(IZeroTouchMediaCredentialCheck MediaCheck, IDomainCredentialVerifier Verifier, ILogger Logger);

    partial void OnIsJoinDeclinedChanged(bool value)
    {
        // A password typed before declining is not needed any more and must not outlive the choice.
        if (value) ClearPassword();
        ResetCredentialCheck();
        RaiseDerivedStateChanged();
        StartZeroTouchCheck();
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
        OnPropertyChanged(nameof(CredentialCheckSeverity));
        OnPropertyChanged(nameof(IsCredentialCheckShown));
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
        // A superseded Zero-touch check stops before it reaches the domain when it has not bound yet.
        zeroTouchCancellation?.Cancel();
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
