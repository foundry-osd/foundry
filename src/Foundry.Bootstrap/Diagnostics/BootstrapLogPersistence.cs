// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;
using Serilog;

namespace Foundry.Bootstrap.Diagnostics;

/// <summary>Copies readable session logs atomically to the selected cache volume.</summary>
internal sealed class BootstrapLogPersistence(string sourceLogDirectory, string? persistenceDirectory, ILogger logger)
    : IBootstrapLogPersistence
{
    public async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (persistenceDirectory is null || !Directory.Exists(sourceLogDirectory)) { return; }
        try
        {
            await DiagnosticLogSnapshot.CopyAsync(sourceLogDirectory, persistenceDirectory, "Foundry*.log", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Could not persist WinPE session logs to the cache volume");
        }
    }
}
