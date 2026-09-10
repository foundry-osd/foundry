// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.Bootstrap.Diagnostics;

/// <summary>Closes logging without allowing sink shutdown to delay or replace the boot outcome.</summary>
internal static class BootstrapLogShutdown
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(2);

    internal static Task FlushAsync() => FlushAsync(() => Log.CloseAndFlushAsync().AsTask(), ShutdownBudget);

    internal static async Task FlushAsync(Func<Task> flush, TimeSpan timeout)
    {
        try
        {
            await Task.Run(flush).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
