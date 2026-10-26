// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Configuration;

/// <summary>
/// Serializes whole-configuration replacements (import, and later following a publication) with library content cleanup.
/// </summary>
/// <remarks>
/// A cleanup reads the references of the current configuration and then deletes on a background thread. Without this gate a
/// replacement saved in between could reference content the cleanup is deleting. Holders keep the gate only around reading
/// references and deleting, or around <see cref="IFoundryConfigurationStateService.Replace"/>; never around user interaction.
/// </remarks>
public sealed class ConfigurationReplacementGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    /// <summary>Waits for the gate; dispose the result to release it.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int isReleased;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isReleased, 1) == 0) semaphore.Release();
        }
    }
}
