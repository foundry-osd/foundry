// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy;

/// <summary>
/// Ends the Foundry Deploy phase: the first requested exit code wins and completes <see cref="Completion"/>. It never shuts
/// the WPF application down, which the Foundry.WinPE UI host owns. Thread-safe.
/// </summary>
internal sealed class DeployPhaseLifetime
{
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes with the winning exit code; its continuations never run on the requesting thread.</summary>
    public Task<int> Completion => _completion.Task;

    /// <summary>Requests the end of the phase with <paramref name="exitCode"/>.</summary>
    /// <returns><see langword="true"/> when this request decided the exit code.</returns>
    public bool TryEnd(int exitCode) => _completion.TrySetResult(exitCode);
}
