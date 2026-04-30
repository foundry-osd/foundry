namespace Foundry.Models.Configuration;

public sealed record LocalizationSettings
{
    public IReadOnlyList<string> VisibleLanguageCodes { get; init; } = Array.Empty<string>();
    public string? DefaultLanguageCodeOverride { get; init; }
    public string? DefaultTimeZoneId { get; init; }
    public bool ForceSingleVisibleLanguage { get; init; }
}
