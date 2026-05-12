namespace Foundry.Services.Localization;

/// <summary>
/// Describes an application language transition.
/// </summary>
/// <param name="oldLanguage">Previously active language code.</param>
/// <param name="newLanguage">Newly active language code.</param>
public sealed class ApplicationLanguageChangedEventArgs(string oldLanguage, string newLanguage) : EventArgs
{
    /// <summary>
    /// Gets the previously active language code.
    /// </summary>
    public string OldLanguage { get; } = oldLanguage;

    /// <summary>
    /// Gets the newly active language code.
    /// </summary>
    public string NewLanguage { get; } = newLanguage;
}
