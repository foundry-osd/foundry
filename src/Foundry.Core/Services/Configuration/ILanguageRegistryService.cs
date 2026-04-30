using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public interface ILanguageRegistryService
{
    IReadOnlyList<LanguageRegistryEntry> GetLanguages();
}
