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

    public static bool RequiresProtectedMedia(OobeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.IsEnabled &&
               ((settings.EnableAdministratorAccount && settings.UseAdministratorPassword) ||
                settings.AdditionalAccounts.Any(account => account.UsePassword));
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
        HashSet<string> accountIds = new(StringComparer.Ordinal);
        HashSet<string> userNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (OobeAdditionalAccountSettings account in settings.AdditionalAccounts)
        {
            ValidateAccount(account, accountIds, userNames, secretState, issues);
        }

        if (settings.EnableAdministratorAccount &&
            settings.UseAdministratorPassword &&
            secretState is not null &&
            !secretState.HasAdministratorPassword)
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.PasswordRequired,
                IsAdministratorAccount: true));
        }

        if (settings.EnableAdministratorAccount &&
            settings.UseAdministratorPassword &&
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
        ThrowIfInvalid(settings, secretState: null);
    }

    public static void ThrowIfInvalid(OobeSettings settings, OobeAccountSecretState? secretState)
    {
        OobeAccountConfigurationValidationResult result = Validate(settings, secretState);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("The OOBE local account configuration is invalid.");
        }
    }

    private static void ValidateAccount(
        OobeAdditionalAccountSettings account,
        ISet<string> accountIds,
        ISet<string> userNames,
        OobeAccountSecretState? secretState,
        ICollection<OobeAccountConfigurationValidationIssue> issues)
    {
        string? accountId = account.Id;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.AccountIdRequired));
        }
        else if (!accountIds.Add(accountId))
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.DuplicateAccountId,
                accountId));
        }

        string? userName = account.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.UserNameRequired,
                accountId));
        }
        else
        {
            if (!userNames.Add(userName))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.DuplicateUserName,
                    accountId));
            }

            if (ReservedBuiltInUserNames.Contains(userName))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.ReservedBuiltInUserName,
                    accountId));
            }

            if (userName.AsSpan().IndexOfAny(InvalidUserNameCharacters) >= 0)
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.InvalidUserNameCharacters,
                    accountId));
            }

            if (userName.EndsWith(' ') || userName.EndsWith('.'))
            {
                issues.Add(new OobeAccountConfigurationValidationIssue(
                    OobeAccountConfigurationValidationCode.TrailingPeriodOrSpace,
                    accountId));
            }
        }

        if (!string.IsNullOrWhiteSpace(accountId) &&
            account.UsePassword &&
            secretState is not null &&
            !secretState.HasAdditionalAccountPassword(accountId))
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.PasswordRequired,
                accountId));
        }

        if (!string.IsNullOrWhiteSpace(accountId) &&
            account.UsePassword &&
            secretState?.HasAdditionalAccountPasswordConfirmationMismatch(accountId) == true)
        {
            issues.Add(new OobeAccountConfigurationValidationIssue(
                OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch,
                accountId));
        }
    }
}
