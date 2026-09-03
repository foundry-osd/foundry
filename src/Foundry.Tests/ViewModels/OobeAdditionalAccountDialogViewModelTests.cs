// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Localization;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.ViewModels;

namespace Foundry.Tests.ViewModels;

public sealed class OobeAdditionalAccountDialogViewModelTests
{
    [Fact]
    public void TryCreateResult_WhenUserNameDuplicatesExistingAccount_ReturnsLocalizedValidationMessage()
    {
        var localizationService = new TestLocalizationService();
        var viewModel = new OobeAdditionalAccountDialogViewModel(
            localizationService,
            account: null,
            existingAccounts: new OobeAdditionalAccountSettings[]
            {
                new OobeAdditionalAccountSettings
                {
                    Id = "existing-account",
                    UserName = "Technician",
                    Type = OobeAccountType.Standard
                }
            });

        viewModel.UserName = "technician";

        OobeAdditionalAccountDialogResult? result = viewModel.TryCreateResult("Password1!", "Password1!", out string validationMessage);

        Assert.Null(result);
        Assert.Equal("Choose a different local account name.", validationMessage);
    }

    [Fact]
    public void Constructor_WhenAddingAccount_EnablesPasswordByDefault()
    {
        var viewModel = new OobeAdditionalAccountDialogViewModel(
            new TestLocalizationService(),
            account: null,
            existingAccounts: []);

        Assert.True(viewModel.UsePassword);
    }

    [Fact]
    public void TryCreateResult_WhenPasswordIsDisabled_ReturnsEmptySecrets()
    {
        var localizationService = new TestLocalizationService();
        var viewModel = new OobeAdditionalAccountDialogViewModel(
            localizationService,
            account: null,
            existingAccounts: []);

        viewModel.UserName = "Technician";
        viewModel.UsePassword = false;

        using OobeAdditionalAccountDialogResult? result = viewModel.TryCreateResult(string.Empty, string.Empty, out string validationMessage);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, validationMessage);
        Assert.Empty(result.Password);
        Assert.Empty(result.Confirmation);
        Assert.Equal("Technician", result.Account.UserName);
        Assert.False(result.Account.UsePassword);
    }

    private sealed class TestLocalizationService : IApplicationLocalizationService
    {
        public string CurrentLanguage => "en-US";

        public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetString(string key) => key switch
        {
            "Customization.OobeAdditionalAccountDialog.AddTitle" => "Add local account",
            "Customization.OobeAdditionalAccountDialog.EditTitle" => "Edit local account",
            "Customization.OobeAdditionalAccountDialog.Description" => "Configure the account and optional password.",
            "Customization.OobeAdditionalAccountDialog.UserNameLabel" => "Username",
            "Proxy_UsernameTextBox.PlaceholderText" => "Username",
            "Customization.OobeAdditionalAccountDialog.AccountTypeLabel" => "Account type",
            "Customization.OobeAccountPasswordLabel" => "Set a password",
            "Customization.OobeAccountPasswordDescription" => "Set a predefined password.",
            "GeneralConfiguration.DeploymentProtection.Password.Placeholder" => "Password",
            "GeneralConfiguration.DeploymentProtection.Confirmation.Placeholder" => "Confirm password",
            "Customization.OobeAdditionalAccountDialog.Save" => "Add account",
            "Customization.OobeAdditionalAccountDialog.Update" => "Save changes",
            "Common.Cancel" => "Cancel",
            "Customization.OobeAccountTypeStandard" => "Standard",
            "Customization.OobeAccountTypeAdministrator" => "Administrator",
            "Customization.OobeAccounts.Validation.UserNameRequired" => "Enter a local account name.",
            "Customization.OobeAccounts.Validation.DuplicateUserName" => "Choose a different local account name.",
            "Customization.OobeAccounts.Validation.ReservedBuiltInUserName" => "This Windows account name is reserved.",
            "Customization.OobeAccounts.Validation.InvalidUserNameCharacters" => "The account name contains characters Windows does not allow.",
            "Customization.OobeAccounts.Validation.TrailingPeriodOrSpace" => "The account name cannot end with a period or space.",
            "Customization.OobeAccounts.Validation.PasswordConfirmationMismatch" => "Password and confirmation must match.",
            "Customization.OobeAccounts.Validation.PasswordRequired" => "Enter and confirm a password.",
            "Customization.OobeAccounts.Validation.Generic" => "Review this local account.",
            _ => key
        };

        public string FormatString(string key, params object[] args)
        {
            return string.Format(GetString(key), args);
        }

        public IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions() => [];
    }
}
