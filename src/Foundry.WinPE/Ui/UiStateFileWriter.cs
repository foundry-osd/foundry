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
/// started by the guardian) nothing is written, and a write failure is logged and never fails the session. Thread-safe: the
/// destructive step is recorded from the deployment thread.
/// </summary>
/// <remarks>
/// A failed write is retried once. When the record of a started destructive step still fails, the state file is deleted:
/// the guardian then reads an unknown state and never offers "Relaunch", where the previous record would have offered it.
/// </remarks>
/// <param name="path">The state file, or <see langword="null"/> without a guardian.</param>
/// <param name="logger">The host log.</param>
/// <param name="write">Writes the file; <see cref="UiStateFile.Write"/> by default.</param>
/// <param name="delete">Deletes the file; <see cref="File.Delete"/> by default.</param>
internal sealed class UiStateFileWriter(string? path, ILogger logger, Action<string, UiState>? write = null,
    Action<string>? delete = null) : IUiStateWriter
{
    private readonly Lock _gate = new();
    private readonly Action<string, UiState> _write = write ?? UiStateFile.Write;
    private readonly Action<string> _delete = delete ?? File.Delete;

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
            if (TryWrite(path, state, retrying: false) || TryWrite(path, state, retrying: true) || !state.DestructiveStarted)
            {
                return;
            }

            logger.Error("The destructive step could not be recorded. The UI state file is removed so the guardian never offers Relaunch.");
            try
            {
                _delete(path);
            }
            catch (Exception exception)
            {
                logger.Error(
                    "The UI state file could not be removed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                    exception.GetType().Name, exception.HResult);
            }
        }
    }

    private bool TryWrite(string statePath, UiState state, bool retrying)
    {
        try
        {
            _write(statePath, state);
            logger.Debug("UI state recorded. Phase={Phase}, DestructiveStarted={DestructiveStarted}, Culture={Culture}", state.Phase, state.DestructiveStarted, state.Culture);
            return true;
        }
        catch (Exception exception)
        {
            logger.Warning(
                "UI state could not be recorded. Phase={Phase}, DestructiveStarted={DestructiveStarted}, Retrying={Retrying}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                state.Phase, state.DestructiveStarted, !retrying, exception.GetType().Name, exception.HResult);
            return false;
        }
    }
}
