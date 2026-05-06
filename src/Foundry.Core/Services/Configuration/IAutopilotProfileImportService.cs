using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public interface IAutopilotProfileImportService
{
    Task<AutopilotProfileSettings> ImportFromJsonFileAsync(string filePath, CancellationToken cancellationToken = default);
}
