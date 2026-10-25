// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Resource keys, under <c>Sharing.</c>, of the texts that name the secret entered in <see cref="NewSecretFields"/>.</summary>
public sealed record NewSecretTexts(string LabelKey, string ConfirmationLabelKey, string MismatchKey, string CopiedKey)
{
    /// <summary>Gets the texts of the export passphrase.</summary>
    public static NewSecretTexts ExportPassphrase { get; } = new("PassphraseLabel", "ConfirmPassphraseLabel", "PassphraseMismatch", "PassphraseCopied");

    /// <summary>Returns the texts of a publication password whose field is labelled by <paramref name="labelKey"/>.</summary>
    public static NewSecretTexts PublicationPassword(string labelKey) => new(labelKey, "ConfirmPublicationPasswordLabel", "PasswordMismatch", "PasswordCopied");
}

/// <summary>
/// A new secret with its confirmation, the <see cref="PassphrasePolicy"/> rule, generation and copy: the export passphrase or a
/// new publication password. The owner clears it when its dialog closes.
/// </summary>
public sealed partial class NewSecretFields(IApplicationLocalizationService localization, NewSecretTexts texts) : ObservableObject
{
    private bool isGenerating;

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

    /// <summary>Gets whether the secret satisfies the policy and its confirmation matches.</summary>
    public bool IsComplete => IsValid && string.Equals(Password, Confirmation, StringComparison.Ordinal);

    /// <summary>Gets whether the secret can be copied: it satisfies the policy.</summary>
    public bool CanCopy => IsValid;

    /// <summary>Gets the rule the secret still breaks, or the mismatch once a confirmation is typed; empty while typing starts.</summary>
    public string ValidationMessage =>
        Password.Length > 0 && !IsValid ? Text("PassphraseRule")
        : Confirmation.Length > 0 && !string.Equals(Password, Confirmation, StringComparison.Ordinal) ? Text(texts.MismatchKey)
        : string.Empty;

    public bool HasValidationMessage => ValidationMessage.Length > 0;
    public string PasswordLabel => Text(texts.LabelKey);
    public string ConfirmationLabel => Text(texts.ConfirmationLabelKey);
    public string RuleText => Text("PassphraseRule");
    public string GenerateLabel => Text("GeneratePassphrase");
    public string CopyLabel => Text("CopyPassphrase");
    public string CopiedText => Text(texts.CopiedKey);

    private bool IsValid => PassphrasePolicy.Validate(Password) == PassphraseValidation.Valid;

    /// <summary>Fills both fields with a generated secret and shows it so it can be noted or copied.</summary>
    [RelayCommand]
    private void Generate()
    {
        string generated = PassphrasePolicy.Generate();
        isGenerating = true;
        Password = generated;
        Confirmation = generated;
        isGenerating = false;
        IsRevealed = true;
    }

    partial void OnPasswordChanged(string value)
    {
        IsCopied = false;
        HideTypedPassword();
    }

    partial void OnConfirmationChanged(string value) => HideTypedPassword();

    // A generated secret is shown once; typing hides the fields again behind the reveal button.
    private void HideTypedPassword()
    {
        if (!isGenerating) IsRevealed = false;
    }

    /// <summary>Records that the secret was copied; the view owns the clipboard.</summary>
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
