// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Provides session-only OOBE secrets and notifies authoring views when they change.
/// </summary>
public interface IOobeAccountSecretStateService
{
    event EventHandler? Changed;

    void SetAdministratorPassword(string? value);

    void SetAdministratorConfirmation(string? value);

    char[] GetAdministratorPasswordCopy();

    char[] GetAdministratorConfirmationCopy();

    void SetAdditionalAccountPassword(string accountId, ReadOnlySpan<char> value);

    void SetAdditionalAccountConfirmation(string accountId, ReadOnlySpan<char> value);

    char[] GetAdditionalAccountPasswordCopy(string accountId);

    char[] GetAdditionalAccountConfirmationCopy(string accountId);

    OobeAccountConfigurationValidationResult Validate(OobeSettings settings);

    void Update(OobeSettings settings);
}
