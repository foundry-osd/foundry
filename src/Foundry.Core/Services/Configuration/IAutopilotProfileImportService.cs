// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public interface IAutopilotProfileImportService
{
    Task<AutopilotProfileSettings> ImportFromJsonFileAsync(string filePath, CancellationToken cancellationToken = default);
}
