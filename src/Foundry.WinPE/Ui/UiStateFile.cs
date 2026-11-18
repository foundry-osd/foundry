// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.WinPE.Ui;

/// <summary>The part of the UI process session that runs, as recorded in the UI state file.</summary>
internal enum UiPhase
{
    /// <summary>Foundry Connect, the first phase.</summary>
    Connect,

    /// <summary>System preparation (clock and time zone) between Foundry Connect and Foundry Deploy.</summary>
    System,

    /// <summary>Foundry Deploy, the last phase.</summary>
    Deploy
}

/// <summary>What the guardian needs to classify a UI process that ended unexpectedly.</summary>
/// <param name="Phase">The phase that runs.</param>
/// <param name="DestructiveStarted">
/// Whether a live deployment reached the step that can wipe the target disk. Once set, it stays set for the whole session.
/// </param>
internal sealed record UiState(UiPhase Phase, bool DestructiveStarted)
{
    /// <summary>Gets the state of a UI process that has not recorded anything yet.</summary>
    internal static UiState Initial { get; } = new(UiPhase.Connect, DestructiveStarted: false);
}

/// <summary>
/// Reads and writes the UI state file, <c>{ "phase": "connect" | "system" | "deploy", "destructiveStarted": bool }</c>, whose
/// path the guardian gives the UI process in <see cref="PathEnvironmentVariable"/>.
/// </summary>
/// <remarks>
/// A write goes to a temporary file in the same directory, flushed to disk, which then replaces the state file, so a reader
/// sees the previous state or the new one, never a partial document.
/// </remarks>
internal static class UiStateFile
{
    /// <summary>The environment variable holding the full path of the state file, set by the guardian for each launch.</summary>
    internal const string PathEnvironmentVariable = "FOUNDRY_WINPE_UI_STATE_PATH";

    private const int MaximumBytes = 4096;
    private const string PhaseProperty = "phase";
    private const string DestructiveStartedProperty = "destructiveStarted";

    /// <summary>Atomically replaces the state file at <paramref name="path"/>, creating its directory when needed.</summary>
    /// <exception cref="IOException">The file or its directory cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory cannot be written.</exception>
    internal static void Write(string path, UiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The UI state path must be absolute.", nameof(path));
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            [PhaseProperty] = ToName(state.Phase),
            [DestructiveStartedProperty] = state.DestructiveStarted
        });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Reads the state file. A missing, unreadable or invalid file is <see langword="null"/>, an unknown state the caller must
    /// not treat as safe: the UI session records its state before it shows any window, so after readiness a missing file
    /// means a failed write.
    /// </summary>
    internal static UiState? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumBytes)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(PhaseProperty, out JsonElement phase) || phase.ValueKind != JsonValueKind.String ||
                !TryParsePhase(phase.GetString(), out UiPhase parsedPhase) ||
                !root.TryGetProperty(DestructiveStartedProperty, out JsonElement destructive) ||
                destructive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return new UiState(parsedPhase, destructive.GetBoolean());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string ToName(UiPhase phase) => phase switch
    {
        UiPhase.Connect => "connect",
        UiPhase.System => "system",
        UiPhase.Deploy => "deploy",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    private static bool TryParsePhase(string? name, out UiPhase phase)
    {
        (bool parsed, phase) = name switch
        {
            "connect" => (true, UiPhase.Connect),
            "system" => (true, UiPhase.System),
            "deploy" => (true, UiPhase.Deploy),
            _ => (false, UiPhase.Connect)
        };
        return parsed;
    }
}
