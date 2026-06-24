// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public interface IDriverPackExtractionService
{
    Task<DriverPackExtractionResult> ExtractAsync(
        DriverPackExecutionPlan executionPlan,
        string extractionRootPath,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
