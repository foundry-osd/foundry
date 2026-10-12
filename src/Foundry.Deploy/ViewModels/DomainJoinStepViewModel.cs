// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
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
        !IsTypedOuInvalid;

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
    public void RefreshLocalizedText() => RebuildDomainChoices();

    /// <summary>Replaces the owned password with a copy of the supplied characters.</summary>
    public void SetPassword(ReadOnlySpan<char> value)
    {
        ErasePassword();
        password = value.IsEmpty ? null : value.ToArray();
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

    partial void OnSelectedDomainChanged(DeployDomainJoinDomainSettings? value) => ApplyRetainedDomain();

    partial void OnIsJoinDeclinedChanged(bool value)
    {
        // A password typed before declining is not needed any more and must not outlive the choice.
        if (value) ClearPassword();
        RaiseDerivedStateChanged();
    }

    partial void OnDomainNameChanged(string value) => RaiseDerivedStateChanged();

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

    partial void OnAccountNameChanged(string value) => RaiseDerivedStateChanged();
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
        OnPropertyChanged(nameof(EffectiveOuDistinguishedName));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
