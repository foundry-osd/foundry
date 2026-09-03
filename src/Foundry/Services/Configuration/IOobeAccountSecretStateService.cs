// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

public interface IOobeAccountSecretStateService
{
    event EventHandler? Changed;

    void SetAdministratorPassword(string? value);

    void SetAdministratorPassword(ReadOnlySpan<char> value);

    void SetAdministratorConfirmation(string? value);

    void SetAdministratorConfirmation(ReadOnlySpan<char> value);

    char[] GetAdministratorPasswordCopy();

    char[] GetAdministratorConfirmationCopy();

    void SetAdditionalAccountPassword(string accountId, string? value);

    void SetAdditionalAccountPassword(string accountId, ReadOnlySpan<char> value);

    void SetAdditionalAccountConfirmation(string accountId, string? value);

    void SetAdditionalAccountConfirmation(string accountId, ReadOnlySpan<char> value);

    char[] GetAdditionalAccountPasswordCopy(string accountId);

    char[] GetAdditionalAccountConfirmationCopy(string accountId);

    OobeAccountConfigurationValidationResult Validate(OobeSettings settings);

    void Update(OobeSettings settings);

    void Clear();
}
