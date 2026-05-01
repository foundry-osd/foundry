namespace Foundry.Services.Localization;

public sealed class ApplicationLanguageChangedEventArgs(string oldLanguage, string newLanguage) : EventArgs
{
    public string OldLanguage { get; } = oldLanguage;
    public string NewLanguage { get; } = newLanguage;
}
