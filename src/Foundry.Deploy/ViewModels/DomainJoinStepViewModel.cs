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
/// Holds what the technician enters on the Domain Join wizard step: credentials in Interactive mode and the OU
/// when the media lets the technician choose one. The password is an owned buffer that never enters wizard
/// state, logs or the deployment request.
/// </summary>
public sealed partial class DomainJoinStepViewModel : ObservableObject, IDisposable
{
    private DeployDomainJoinSettings settings = new();
    private char[]? password;

    /// <summary>Raised when an input or its validity changes, so the wizard can refresh navigation.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when the owned password is erased, so the view can empty its password box.</summary>
    public event EventHandler? PasswordCleared;

    [ObservableProperty]
    private string domainName = string.Empty;

    [ObservableProperty]
    private string accountName = string.Empty;

    [ObservableProperty]
    private DomainJoinOrganizationalUnitSettings? selectedOrganizationalUnit;

    [ObservableProperty]
    private string typedOuDistinguishedName = string.Empty;

    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits => settings.OrganizationalUnits;

    /// <summary>Gets whether the technician supplies the domain, account and password.</summary>
    public bool RequiresCredentials => settings.IsEnabled && settings.Mode == DomainJoinMode.Interactive;

    public bool IsDomainReadOnly => !RequiresCredentials;

    /// <summary>Gets whether the technician picks an OU from the saved list.</summary>
    public bool IsOuListVisible => settings.AllowOuSelectionDuringDeployment && HasUsableOuList;

    /// <summary>Gets whether an OU may be typed, which is only offered when no saved list applies to the domain.</summary>
    public bool IsTypedOuVisible => RequiresCredentials && !HasUsableOuList;

    /// <summary>Gets whether the step has anything for the technician to enter or choose.</summary>
    public bool HasInput => settings.IsEnabled &&
        (RequiresCredentials || settings.AllowOuSelectionDuringDeployment &&
            DomainJoinPreparationService.HasCompatibleCatalog(settings, settings.DomainName));

    public bool HasPassword => password is { Length: > 0 };

    public bool IsDomainInvalid => DomainName.Length > 0 && !DomainJoinCredentialContext.IsValidDomainName(DomainName);

    public bool IsAccountInvalid => AccountName.Length > 0 && !DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName);

    public bool IsTypedOuInvalid => IsTypedOuVisible && TypedOu.Length > 0 &&
        !(DistinguishedNameRules.IsOrganizationalUnit(TypedOu) && DistinguishedNameRules.IsWithinDomain(TypedOu, DomainName));

    /// <summary>Gets whether the wizard may leave the step and start a deployment with these inputs.</summary>
    public bool IsValid => !HasInput ||
        DomainJoinCredentialContext.IsValidDomainName(DomainName) &&
        (!RequiresCredentials || DomainJoinConfigurationValidator.IsQualifiedAccount(AccountName) && HasPassword) &&
        (!IsOuListVisible || SelectedOrganizationalUnit is not null) &&
        !IsTypedOuInvalid;

    /// <summary>Gets the OU the join will use; <see langword="null"/> means the domain's default location.</summary>
    public string? EffectiveOuDistinguishedName =>
        IsOuListVisible ? SelectedOrganizationalUnit?.DistinguishedName :
        IsTypedOuVisible ? (TypedOu.Length > 0 && !IsTypedOuInvalid ? TypedOu : null) :
        DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings, DomainName)?.DistinguishedName;

    private bool HasUsableOuList => DomainJoinPreparationService.HasCompatibleCatalog(settings, DomainName);

    private string TypedOu => TypedOuDistinguishedName.Trim();

    /// <summary>Loads the media configuration and discards every earlier input, including the password.</summary>
    public void Configure(DeployDomainJoinSettings domainJoin)
    {
        settings = domainJoin ?? throw new ArgumentNullException(nameof(domainJoin));
        ClearPassword();
        AccountName = string.Empty;
        TypedOuDistinguishedName = string.Empty;
        DomainName = settings.DomainName ?? string.Empty;
        SelectedOrganizationalUnit = DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings, DomainName);
        OnPropertyChanged(nameof(OrganizationalUnits));
        OnPropertyChanged(nameof(HasInput));
        RaiseDerivedStateChanged();
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
        DomainName,
        RequiresCredentials ? AccountName : string.Empty,
        IsOuListVisible ? SelectedOrganizationalUnit?.Id : null,
        RequiresCredentials ? password : null,
        IsTypedOuVisible && TypedOu.Length > 0 ? TypedOu : null);

    public void Dispose() => ErasePassword();

    partial void OnDomainNameChanged(string value)
    {
        // A saved OU only applies to the domain its list was built for.
        if (!HasUsableOuList) SelectedOrganizationalUnit = null;
        else SelectedOrganizationalUnit ??= DomainJoinPreparationService.ResolveDefaultOrganizationalUnit(settings, value);
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
