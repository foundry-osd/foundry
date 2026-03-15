using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface ILanguageRegistryService
{
    IReadOnlyList<LanguageRegistryEntry> GetLanguages();
}
