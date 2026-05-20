namespace Foundry.Localization;

/// <summary>
/// Creates the shared UI culture catalog used by Foundry desktop applications.
/// </summary>
public static class FoundrySupportedCultures
{
    /// <summary>
    /// Gets the default UI culture used by Foundry desktop applications.
    /// </summary>
    public const string DefaultCultureCode = "en-US";

    /// <summary>
    /// Creates the supported culture catalog for the current Foundry desktop resource set.
    /// </summary>
    /// <returns>A catalog containing the UI cultures currently shipped by the apps.</returns>
    public static SupportedCultureCatalog CreateCatalog()
    {
        return new SupportedCultureCatalog(
            DefaultCultureCode,
            [
                new SupportedCultureDefinition(DefaultCultureCode, "Language.English", 10),
                new SupportedCultureDefinition("fr-FR", "Language.French", 20)
            ]);
    }
}
