// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.WinPE.Ui;

/// <summary>Records the UI process state for the guardian. Never throws.</summary>
internal interface IUiStateWriter
{
    /// <summary>Records <paramref name="state"/>; a failure is logged and never reaches the caller.</summary>
    void Write(UiState state);
}

/// <summary>
/// Writes the state to the <see cref="UiStateFile"/> the guardian named, best effort: without a path (the UI process was not
/// started by the guardian) nothing is written, and a write failure is logged as a warning and never fails the session.
/// Thread-safe: the destructive step is recorded from the deployment thread.
/// </summary>
internal sealed class UiStateFileWriter(string? path, ILogger logger) : IUiStateWriter
{
    private readonly Lock _gate = new();

    /// <summary>Creates the writer for the path in <see cref="UiStateFile.PathEnvironmentVariable"/>, if any.</summary>
    internal static UiStateFileWriter FromEnvironment(ILogger logger) =>
        new(Environment.GetEnvironmentVariable(UiStateFile.PathEnvironmentVariable), logger);

    /// <inheritdoc />
    public void Write(UiState state)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                UiStateFile.Write(path, state);
                logger.Debug("UI state recorded. Phase={Phase}, DestructiveStarted={DestructiveStarted}",
                    state.Phase, state.DestructiveStarted);
            }
            catch (Exception exception)
            {
                logger.Warning(
                    "UI state could not be recorded. Phase={Phase}, DestructiveStarted={DestructiveStarted}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                    state.Phase, state.DestructiveStarted, exception.GetType().Name, exception.HResult);
            }
        }
    }
}
