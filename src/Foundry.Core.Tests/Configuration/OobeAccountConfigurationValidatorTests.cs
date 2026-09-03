// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class OobeAccountConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenAdditionalAccountNameIsEmpty_ReturnsRequiredIssue()
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(new OobeAdditionalAccountSettings
            {
                Id = "account-1",
                UserName = " ",
                Type = OobeAccountType.Standard
            }));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.UserNameRequired &&
            issue.AccountId == "account-1");
    }

    [Fact]
    public void Validate_WhenAdditionalAccountIdIsMissing_ReturnsRequiredIssue()
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(new OobeAdditionalAccountSettings
            {
                Id = " ",
                UserName = "Technician",
                Type = OobeAccountType.Standard
            }));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.AccountIdRequired &&
            issue.AccountId is null);
    }

    [Fact]
    public void Validate_WhenAdditionalAccountIdsAreDuplicated_ReturnsDuplicateIssue()
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(
                CreateAccount("account-1", "Technician"),
                CreateAccount("account-1", "Support")));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.DuplicateAccountId &&
            issue.AccountId == "account-1");
    }

    [Fact]
    public void Validate_WhenAdditionalAccountNamesDifferOnlyByCase_ReturnsDuplicateIssue()
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(
                CreateAccount("account-1", "Technician"),
                CreateAccount("account-2", "technician")));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.DuplicateUserName &&
            issue.AccountId == "account-2");
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Guest")]
    [InlineData("DefaultAccount")]
    [InlineData("WDAGUtilityAccount")]
    public void Validate_WhenAdditionalAccountUsesReservedBuiltInName_ReturnsReservedNameIssue(string userName)
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(CreateAccount("account-1", userName)));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.ReservedBuiltInUserName &&
            issue.AccountId == "account-1");
    }

    [Theory]
    [InlineData("Tech.")]
    [InlineData("Tech ")]
    public void Validate_WhenAdditionalAccountNameEndsWithPeriodOrSpace_ReturnsTrailingCharacterIssue(string userName)
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(CreateAccount("account-1", userName)));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.TrailingPeriodOrSpace &&
            issue.AccountId == "account-1");
    }

    [Theory]
    [InlineData("Tech/User")]
    [InlineData("Tech<User")]
    [InlineData("Tech|User")]
    public void Validate_WhenAdditionalAccountNameContainsInvalidWindowsCharacters_ReturnsInvalidCharacterIssue(string userName)
    {
        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(CreateAccount("account-1", userName)));

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.InvalidUserNameCharacters &&
            issue.AccountId == "account-1");
    }

    [Fact]
    public void Validate_WhenAdministratorPasswordConfirmationDoesNotMatch_ReturnsPasswordMismatchIssue()
    {
        using var state = new OobeAccountSecretState();
        state.SetAdministratorPassword("Password1!");
        state.SetAdministratorConfirmation("Password2!");

        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings([], enableAdministratorAccount: true),
            state);

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch &&
            issue.IsAdministratorAccount);
    }

    [Fact]
    public void Validate_WhenAdditionalAccountPasswordConfirmationDoesNotMatch_ReturnsPasswordMismatchIssue()
    {
        using var state = new OobeAccountSecretState();
        state.SetAdditionalAccountPassword("account-1", "Password1!");
        state.SetAdditionalAccountConfirmation("account-1", "Password2!");

        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(CreateAccount("account-1", "Technician")),
            state);

        Assert.Contains(result.Issues, issue =>
            issue.Code == OobeAccountConfigurationValidationCode.PasswordConfirmationMismatch &&
            issue.AccountId == "account-1");
    }

    [Fact]
    public void Validate_WhenBlankPasswordsAreConfirmed_DoesNotEnforceStrengthPolicy()
    {
        using var state = new OobeAccountSecretState();
        state.SetAdministratorPassword(string.Empty);
        state.SetAdministratorConfirmation(string.Empty);
        state.SetAdditionalAccountPassword("account-1", string.Empty);
        state.SetAdditionalAccountConfirmation("account-1", string.Empty);

        OobeAccountConfigurationValidationResult result = OobeAccountConfigurationValidator.Validate(
            CreateOobeSettings(
                [CreateAccount("account-1", "Technician")],
                enableAdministratorAccount: true),
            state);

        Assert.True(result.IsValid);
    }

    private static OobeSettings CreateOobeSettings(
        params OobeAdditionalAccountSettings[] accounts)
    {
        return CreateOobeSettings(accounts, enableAdministratorAccount: false);
    }

    private static OobeSettings CreateOobeSettings(
        OobeAdditionalAccountSettings[] accounts,
        bool enableAdministratorAccount)
    {
        return new OobeSettings
        {
            IsEnabled = true,
            EnableAdministratorAccount = enableAdministratorAccount,
            AdditionalAccounts = accounts
        };
    }

    private static OobeAdditionalAccountSettings CreateAccount(string id, string userName)
    {
        return new OobeAdditionalAccountSettings
        {
            Id = id,
            UserName = userName,
            Type = OobeAccountType.Standard
        };
    }
}
