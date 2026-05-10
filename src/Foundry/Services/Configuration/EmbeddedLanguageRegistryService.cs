using System.Reflection;
using System.Text.Json;
using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public sealed class EmbeddedLanguageRegistryService : ILanguageRegistryService
{
    private const string ResourceName = "Foundry.Configuration.Languages";

    private readonly Lazy<IReadOnlyList<LanguageRegistryEntry>> _languages = new(LoadLanguages);

    public IReadOnlyList<LanguageRegistryEntry> GetLanguages()
    {
        return _languages.Value;
    }

    private static IReadOnlyList<LanguageRegistryEntry> LoadLanguages()
    {
        Assembly assembly = typeof(EmbeddedLanguageRegistryService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded language registry resource '{ResourceName}' was not found.");
        }

        LanguageRegistryEntry[]? languages = JsonSerializer.Deserialize<LanguageRegistryEntry[]>(
            stream,
            ConfigurationJsonDefaults.SerializerOptions);

        LanguageRegistryEntry[] entries = (languages ?? Array.Empty<LanguageRegistryEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Code))
            .Select(entry => entry with
            {
                Code = LanguageCodeUtility.Canonicalize(entry.Code),
                DisplayName = entry.DisplayName.Trim(),
                EnglishName = entry.EnglishName.Trim()
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Code))
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] duplicateCodes = entries
            .GroupBy(entry => LanguageCodeUtility.NormalizeForComparison(entry.Code), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().Code)
            .ToArray();

        if (duplicateCodes.Length > 0)
        {
            throw new InvalidOperationException($"Embedded language registry contains duplicate language codes: {string.Join(", ", duplicateCodes)}.");
        }

        return entries;
    }
}
