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

namespace Foundry.Deploy.ViewModels;

/// <summary>
/// Holds what the technician enters on the Domain Join wizard step: the domain when the media lists several,
/// credentials in Interactive mode, and the OU when the retained domain lists several. The password is an owned
/// buffer that never enters wizard state, logs or the deployment request.
/// </summary>
public sealed partial class DomainJoinStepViewModel : ObservableObject, IDisposable
{
    private DeployDomainJoinSettings settings = new();
    private char[]? password;

    /// <summary>Raised when an input or its validity changes, so the wizard can refresh navigation.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when the owned password is erased, so the view can empty its password box.</summary>
    public event EventHandler? PasswordCleared;

    /// <summary>Listed domain shown in the domain list; the default is preselected.</summary>
    [ObservableProperty]
    private DeployDomainJoinDomainSettings? selectedDomain;

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

    /// <summary>Gets the OUs of the retained domain; they change with the domain.</summary>
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits => RetainedDomain?.OrganizationalUnits ?? [];

    /// <summary>Gets whether the technician supplies the account and password.</summary>
    public bool RequiresCredentials => settings.IsEnabled && settings.Mode == DomainJoinMode.Interactive;

    /// <summary>Gets whether the technician picks the domain, which is the case when the media lists more than one.</summary>
    public bool IsDomainListVisible => settings.IsEnabled && settings.Domains.Count > 1;

    /// <summary>Gets whether the domain is shown as text: read-only for a listed domain, editable when the media lists none.</summary>
    public bool IsDomainTextVisible => !IsDomainListVisible;

    public bool IsDomainReadOnly => HasListedDomains;

    /// <summary>Gets whether the technician picks an OU: the retained domain lists more than one. A single listed OU is used as is.</summary>
    public bool IsOuListVisible => OrganizationalUnits.Count > 1;

    /// <summary>Gets whether an OU may be typed, which is only offered in Interactive mode for a domain without listed OUs.</summary>
    public bool IsTypedOuVisible => RequiresCredentials && OrganizationalUnits.Count == 0;

    /// <summary>Gets whether the step has anything for the technician to enter or choose.</summary>
    public bool HasInput => settings.IsEnabled && (RequiresCredentials || IsDomainListVisible || IsOuListVisible);

    public bool HasPassword => password is { Length: > 0 };

    public bool IsDomainInvalid => !HasListedDomains && DomainName.Length > 0 && !DomainJoinCredentialContext.IsValidDomainName(DomainName);

    public bool IsAccountInvalid => AccountName.Length > 0 && !DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName);

    public bool IsTypedOuInvalid => IsTypedOuVisible && TypedOu.Length > 0 &&
        !(DistinguishedNameRules.IsOrganizationalUnit(TypedOu) && DistinguishedNameRules.IsWithinDomain(TypedOu, DomainName));

    /// <summary>Gets whether the wizard may leave the step and start a deployment with these inputs.</summary>
    public bool IsValid => !HasInput ||
        (HasListedDomains ? RetainedDomain is not null : DomainJoinCredentialContext.IsValidDomainName(DomainName)) &&
        (!RequiresCredentials || DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName) && HasPassword) &&
        (!IsOuListVisible || SelectedOrganizationalUnit is not null) &&
        !IsTypedOuInvalid;

    /// <summary>Gets the OU the join will use; <see langword="null"/> means the domain's default location.</summary>
    public string? EffectiveOuDistinguishedName =>
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
        AccountName = string.Empty;
        TypedOuDistinguishedName = string.Empty;
        OnPropertyChanged(nameof(Domains));
        SelectedDomain = DomainJoinPreparationService.ResolveDomain(settings, null);
        ApplyRetainedDomain();
    }

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
    public DomainJoinSubmission? CreateSubmission() => !settings.IsEnabled ? null : new(
        HasListedDomains ? RetainedDomain?.Id : null,
        DomainName,
        RequiresCredentials ? AccountName : string.Empty,
        IsOuListVisible ? SelectedOrganizationalUnit?.Id : null,
        RequiresCredentials ? password : null,
        IsTypedOuVisible && TypedOu.Length > 0 ? TypedOu : null);

    public void Dispose() => ErasePassword();

    partial void OnSelectedDomainChanged(DeployDomainJoinDomainSettings? value) => ApplyRetainedDomain();

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

    private void ErasePassword()
    {
        if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        password = null;
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(RequiresCredentials));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(IsDomainListVisible));
        OnPropertyChanged(nameof(IsDomainTextVisible));
        OnPropertyChanged(nameof(IsDomainReadOnly));
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
