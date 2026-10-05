// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.DomainJoin;

/// <summary>Shows the dialogs that add organizational units to the Domain Join list.</summary>
public interface IDomainJoinOuDialogService
{
    /// <summary>
    /// Asks for one OU. The dialog stays open while <paramref name="tryAdd"/> returns a message, so the entry can
    /// be corrected in place.
    /// </summary>
    /// <param name="tryAdd">Adds the display name and distinguished name; returns the refusal reason, or null.</param>
    Task ShowAddAsync(Func<string, string, string?> tryAdd);

    /// <summary>Lets the user choose which discovered OUs to add.</summary>
    /// <param name="candidates">OUs found in the authoring computer's domain.</param>
    /// <param name="isIncomplete">Whether the directory returned only part of its OUs.</param>
    /// <returns>The chosen OUs, or <see langword="null"/> when the dialog is canceled.</returns>
    Task<IReadOnlyList<DomainJoinOrganizationalUnitSettings>?> PickAsync(
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates, bool isIncomplete);
}
