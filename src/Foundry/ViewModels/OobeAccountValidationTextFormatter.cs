// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

internal static class OobeAccountValidationTextFormatter
{
    public static string FormatAdministratorIssue(
        IApplicationLocalizationService localizationService,
        OobeAccountConfigurationValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return issue.Code switch
        {
            OobeAccountConfigurationValidationCode.PasswordRequired =>
                localizationService.GetString("Customization.OobeAccounts.Validation.PasswordRequired"),
            OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch =>
                localizationService.GetString("Customization.OobeAccounts.Validation.PasswordConfirmationMismatch"),
            _ => localizationService.GetString("Customization.OobeAccounts.Validation.Generic")
        };
    }

    public static string FormatAdditionalAccountIssue(
        IApplicationLocalizationService localizationService,
        OobeAccountConfigurationValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return issue.Code switch
        {
            OobeAccountConfigurationValidationCode.UserNameRequired =>
                localizationService.GetString("Customization.OobeAccounts.Validation.UserNameRequired"),
            OobeAccountConfigurationValidationCode.UserNameTooLong =>
                localizationService.GetString("Customization.OobeAccounts.Validation.UserNameTooLong"),
            OobeAccountConfigurationValidationCode.DuplicateUserName =>
                localizationService.GetString("Customization.OobeAccounts.Validation.DuplicateUserName"),
            OobeAccountConfigurationValidationCode.ReservedBuiltInUserName =>
                localizationService.GetString("Customization.OobeAccounts.Validation.ReservedBuiltInUserName"),
            OobeAccountConfigurationValidationCode.InvalidUserNameCharacters =>
                localizationService.GetString("Customization.OobeAccounts.Validation.InvalidUserNameCharacters"),
            OobeAccountConfigurationValidationCode.TrailingPeriodOrSpace =>
                localizationService.GetString("Customization.OobeAccounts.Validation.TrailingPeriodOrSpace"),
            OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch =>
                localizationService.GetString("Customization.OobeAccounts.Validation.PasswordConfirmationMismatch"),
            OobeAccountConfigurationValidationCode.PasswordRequired =>
                localizationService.GetString("Customization.OobeAccounts.Validation.PasswordRequired"),
            _ => localizationService.GetString("Customization.OobeAccounts.Validation.Generic")
        };
    }
}
