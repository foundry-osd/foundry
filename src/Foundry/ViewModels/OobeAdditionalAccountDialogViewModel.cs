// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class OobeAdditionalAccountDialogViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationLocalizationService localizationService;
    private readonly IReadOnlyList<OobeAdditionalAccountSettings> existingAccounts;
    private readonly string accountId;

    public OobeAdditionalAccountDialogViewModel(
        IApplicationLocalizationService localizationService,
        OobeAdditionalAccountSettings? account,
        IReadOnlyList<OobeAdditionalAccountSettings> existingAccounts)
    {
        this.localizationService = localizationService;
        this.existingAccounts = existingAccounts ?? throw new ArgumentNullException(nameof(existingAccounts));
        accountId = string.IsNullOrWhiteSpace(account?.Id)
            ? Guid.NewGuid().ToString("N")
            : account.Id;

        UserName = account?.UserName ?? string.Empty;
        UsePassword = account?.UsePassword ?? true;
        RefreshLocalizedText();
        RefreshAccountTypeOptions(account?.Type ?? OobeAccountType.Standard);

        localizationService.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<SelectionOption<OobeAccountType>> AccountTypeOptions { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string UserNameLabel { get; set; }

    [ObservableProperty]
    public partial string AccountTypeLabel { get; set; }

    [ObservableProperty]
    public partial string PasswordLabel { get; set; }

    [ObservableProperty]
    public partial string PasswordDescription { get; set; }

    [ObservableProperty]
    public partial string PasswordPlaceholder { get; set; }

    [ObservableProperty]
    public partial string ConfirmationPlaceholder { get; set; }

    [ObservableProperty]
    public partial string PrimaryButtonText { get; set; }

    [ObservableProperty]
    public partial string CloseButtonText { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; }

    [ObservableProperty]
    public partial bool UsePassword { get; set; }

    [ObservableProperty]
    public partial SelectionOption<OobeAccountType>? SelectedAccountType { get; set; }

    public bool IsEditing => existingAccounts.Any(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));

    public OobeAdditionalAccountDialogResult? TryCreateResult(string password, string confirmation, out string validationMessage)
    {
        using var secretState = new OobeAccountSecretState();
        secretState.SetAdditionalAccountPassword(accountId, password);
        secretState.SetAdditionalAccountConfirmation(accountId, confirmation);

        OobeAdditionalAccountSettings account = new()
        {
            Id = accountId,
            UserName = UserName,
            Type = SelectedAccountType?.Value ?? OobeAccountType.Standard,
            UsePassword = UsePassword
        };

        OobeAccountConfigurationValidationResult validation = OobeAccountConfigurationValidator.Validate(
            new OobeSettings
            {
                IsEnabled = true,
                AdditionalAccounts = existingAccounts
                    .Where(existing => !string.Equals(existing.Id, accountId, StringComparison.Ordinal))
                    .Append(account)
                    .ToArray()
            },
            secretState);

        OobeAccountConfigurationValidationIssue? issue = validation.Issues.FirstOrDefault(entry =>
            string.Equals(entry.AccountId, accountId, StringComparison.Ordinal));
        if (issue is not null)
        {
            validationMessage = OobeAccountValidationTextFormatter.FormatAdditionalAccountIssue(localizationService, issue);
            return null;
        }

        validationMessage = string.Empty;
        return new OobeAdditionalAccountDialogResult(
            account,
            password.ToCharArray(),
            confirmation.ToCharArray());
    }

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void RefreshLocalizedText()
    {
        Title = localizationService.GetString(
            IsEditing
                ? "Customization.OobeAdditionalAccountDialog.EditTitle"
                : "Customization.OobeAdditionalAccountDialog.AddTitle");
        Description = localizationService.GetString("Customization.OobeAdditionalAccountDialog.Description");
        UserNameLabel = localizationService.GetString("Customization.OobeAdditionalAccountDialog.UserNameLabel");
        AccountTypeLabel = localizationService.GetString("Customization.OobeAdditionalAccountDialog.AccountTypeLabel");
        PasswordLabel = localizationService.GetString("Customization.OobeAccountPasswordLabel");
        PasswordDescription = localizationService.GetString("Customization.OobeAccountPasswordDescription");
        PasswordPlaceholder = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Password.Placeholder");
        ConfirmationPlaceholder = localizationService.GetString("GeneralConfiguration.DeploymentProtection.Confirmation.Placeholder");
        PrimaryButtonText = localizationService.GetString(
            IsEditing
                ? "Customization.OobeAdditionalAccountDialog.Update"
                : "Customization.OobeAdditionalAccountDialog.Save");
        CloseButtonText = localizationService.GetString("Common.Cancel");
    }

    private void RefreshAccountTypeOptions(OobeAccountType selectedType)
    {
        AccountTypeOptions.Clear();
        AccountTypeOptions.Add(new(
            OobeAccountType.Standard,
            localizationService.GetString("Customization.OobeAccountTypeStandard")));
        AccountTypeOptions.Add(new(
            OobeAccountType.Administrator,
            localizationService.GetString("Customization.OobeAccountTypeAdministrator")));

        SelectedAccountType = AccountTypeOptions.FirstOrDefault(option => option.Value == selectedType) ?? AccountTypeOptions[0];
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        OobeAccountType selectedType = SelectedAccountType?.Value ?? OobeAccountType.Standard;
        RefreshLocalizedText();
        RefreshAccountTypeOptions(selectedType);
    }
}
