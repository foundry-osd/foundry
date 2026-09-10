// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
            Directory.CreateDirectory(persistenceDirectory);
            foreach (string source in Directory.EnumerateFiles(sourceLogDirectory, "Foundry*.log"))
            {
                string destination = Path.Combine(persistenceDirectory, Path.GetFileName(source));
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 81920, FileOptions.Asynchronous))
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) { File.Delete(temporary); }
                }
            }
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Could not persist WinPE session logs to the cache volume");
        }
    }
}
