// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class OobeAccountConfigurationValidator
{
    private static readonly HashSet<string> ReservedBuiltInUserNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Administrator",
        "DefaultAccount",
        "Guest",
        "HelpAssistant",
        "WDAGUtilityAccount",
        "WSIAccount"
    };

    private static readonly SearchValues<char> InvalidUserNameCharacters =
        SearchValues.Create("\"/\\[]:;|=,+*?<>");

    public static OobeAccountConfigurationValidationResult Validate(OobeSettings settings)
    {
        return Validate(settings, secretState: null);
    }

    public static OobeAccountConfigurationValidationResult Validate(
        OobeSettings settings,
        OobeAccountSecretState? secretState)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return new OobeAccountConfigurationValidationResult([]);
        }

        List<OobeAccountConfigurationValidationIssue> issues = [];
        HashSet<string> userNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (OobeAdditionalAccountSettings account in settings.AdditionalAccounts)
        {
            ValidateAccount(account, userNames, secretState, issues);
        }

        if (settings.EnableAdministratorAccount &&
            secretState?.HasAdministratorPasswordConfirmationMismatch == true)
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch,
                IsAdministratorAccount: true));
        }

        return new OobeAccountConfigurationValidationResult(issues);
    }

    public static void ThrowIfInvalid(OobeSettings settings)
    {
        OobeAccountConfigurationValidationResult result = Validate(settings);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("The OOBE local account configuration is invalid.");
        }
    }

    private static void ValidateAccount(
        OobeAdditionalAccountSettings account,
        ISet<string> userNames,
        OobeAccountSecretState? secretState,
        ICollection<OobeAccountConfigurationValidationIssue> issues)
    {
        string? userName = account.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.UserNameRequired,
                account.Id));
        }
        else
        {
            if (!userNames.Add(userName))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.DuplicateUserName,
                    account.Id));
            }

            if (ReservedBuiltInUserNames.Contains(userName))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.ReservedBuiltInUserName,
                    account.Id));
            }

            if (userName.AsSpan().IndexOfAny(InvalidUserNameCharacters) >= 0)
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.InvalidUserNameCharacters,
                    account.Id));
            }

            if (userName.EndsWith(' ') || userName.EndsWith('.'))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.TrailingPeriodOrSpace,
                    account.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(account.Id) &&
            secretState?.HasAdditionalAccountPasswordConfirmationMismatch(account.Id) == true)
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch,
                account.Id));
        }
    }
}
