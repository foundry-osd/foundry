// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public enum OobeAccountConfigurationValidationCode
{
    UserNameRequired,
    DuplicateUserName,
    ReservedBuiltInUserName,
    InvalidUserNameCharacters,
    TrailingPeriodOrSpace,
    PasswordConfirmationMismatch
}
