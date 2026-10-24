// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration.Legacy;

namespace Foundry.Services.Configuration;

/// <summary>Tracks the one-time notices shown at startup after the legacy migration.</summary>
public interface IMigrationNoticeService
{
    /// <summary>
    /// Returns whether <paramref name="notice"/> is shown: it is pending since the migration, or, for
    /// <see cref="MigrationNotice.PasswordsNeeded"/>, the secret vault could not restore the saved passwords.
    /// </summary>
    bool IsShown(MigrationNotice notice);

    /// <summary>Hides <paramref name="notice"/> and records that it was dismissed.</summary>
    void Dismiss(MigrationNotice notice);
}
