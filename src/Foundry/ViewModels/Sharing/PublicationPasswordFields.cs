// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// A new publication password with its confirmation, the <see cref="PassphrasePolicy"/> rule, generation and copy, shared by the
/// publish and change password dialogs. The owner clears it when its dialog closes.
/// </summary>
public sealed partial class PublicationPasswordFields(IApplicationLocalizationService localization, string passwordLabelKey) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete), nameof(CanCopy), nameof(ValidationMessage), nameof(HasValidationMessage))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete), nameof(ValidationMessage), nameof(HasValidationMessage))]
    public partial string Confirmation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    /// <summary>Gets whether the password satisfies the policy and its confirmation matches.</summary>
    public bool IsComplete => IsValid && string.Equals(Password, Confirmation, StringComparison.Ordinal);

    /// <summary>Gets whether the password can be copied: it satisfies the policy.</summary>
    public bool CanCopy => IsValid;

    /// <summary>Gets the rule the password still breaks, or the mismatch once a confirmation is typed; empty while typing starts.</summary>
    public string ValidationMessage =>
        Password.Length > 0 && !IsValid ? Text("PassphraseRule")
        : Confirmation.Length > 0 && !string.Equals(Password, Confirmation, StringComparison.Ordinal) ? Text("PasswordMismatch")
        : string.Empty;

    public bool HasValidationMessage => ValidationMessage.Length > 0;
    public string PasswordLabel => Text(passwordLabelKey);
    public string ConfirmationLabel => Text("ConfirmPublicationPasswordLabel");
    public string RuleText => Text("PassphraseRule");
    public string GenerateLabel => Text("GeneratePassphrase");
    public string CopyLabel => Text("CopyPassphrase");
    public string CopiedText => Text("PasswordCopied");

    private bool IsValid => PassphrasePolicy.Validate(Password) == PassphraseValidation.Valid;

    /// <summary>Fills both fields with a generated password and shows it so it can be noted or copied.</summary>
    [RelayCommand]
    private void Generate()
    {
        string generated = PassphrasePolicy.Generate();
        Password = generated;
        Confirmation = generated;
        IsRevealed = true;
    }

    partial void OnPasswordChanged(string value) => IsCopied = false;

    /// <summary>Records that the password was copied; the view owns the clipboard.</summary>
    public void MarkCopied() => IsCopied = true;

    /// <summary>Clears both fields.</summary>
    public void Clear()
    {
        Password = string.Empty;
        Confirmation = string.Empty;
    }

    /// <summary>Raises change notifications for the localized texts.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    private string Text(string key) => localization.GetString("Sharing." + key);
}
