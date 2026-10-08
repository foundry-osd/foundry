// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.DomainJoin;

/// <summary>Shows the dialogs that add domains and organizational units to the Domain Join lists.</summary>
public interface IDomainJoinDialogService
{
    /// <summary>
    /// Asks for a domain and, in Zero-touch, the account it joins with. The dialog stays open while
    /// <paramref name="trySave"/> returns a message, so the entry can be corrected in place.
    /// </summary>
    /// <param name="request">What the dialog starts with and which inputs it offers.</param>
    /// <param name="trySave">Saves the input; returns the refusal reason, or null.</param>
    Task ShowDomainAsync(DomainJoinDomainDialogRequest request, Func<DomainJoinDomainDialogInput, string?> trySave);

    /// <summary>
    /// Asks for one OU. The dialog stays open while <paramref name="tryAdd"/> returns a message, so the entry can
    /// be corrected in place.
    /// </summary>
    /// <param name="tryAdd">Adds the display name and distinguished name; returns the refusal reason, or null.</param>
    Task ShowAddAsync(Func<string, string, string?> tryAdd);

    /// <summary>Lets the user choose which discovered OUs to add.</summary>
    /// <param name="candidates">OUs found in the searched domain.</param>
    /// <param name="isIncomplete">Whether the directory returned only part of its OUs.</param>
    /// <returns>The chosen OUs, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<IReadOnlyList<DomainJoinOrganizationalUnitSettings>?> PickAsync(
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates, bool isIncomplete);
}

/// <summary>Describes the domain dialog: a new or existing domain, and whether it asks for a join account.</summary>
/// <param name="IsNew">Whether a domain is being added rather than edited.</param>
/// <param name="AsksForAccount">Whether the join account inputs are shown, which only Zero-touch needs.</param>
/// <param name="CanRename">Whether the name can change; it cannot while the domain lists OUs.</param>
/// <param name="DomainName">The current domain name, empty for a new domain.</param>
/// <param name="AccountName">The current dedicated account; <see langword="null"/> means the shared account.</param>
public sealed record DomainJoinDomainDialogRequest(bool IsNew, bool AsksForAccount, bool CanRename, string DomainName, string? AccountName);

/// <summary>Carries what was entered in the domain dialog. The password is valid only during the save callback.</summary>
public sealed record DomainJoinDomainDialogInput(string DomainName, bool UsesSharedAccount, string AccountName, ReadOnlyMemory<char> Password);