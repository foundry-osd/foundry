namespace Foundry.Core.Models.Configuration.Deploy;

public sealed record DeployLocalizationSettings
{
    public IReadOnlyList<string> VisibleLanguageCodes { get; init; } = Array.Empty<string>();
    public string? DefaultLanguageCodeOverride { get; init; }
    public string? DefaultTimeZoneId { get; init; }
    public bool ForceSingleVisibleLanguage { get; init; }
}
