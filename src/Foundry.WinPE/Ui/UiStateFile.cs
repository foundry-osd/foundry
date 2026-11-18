// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;
using Foundry.Localization;

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
/// <param name="Culture">
/// The UI culture the operator works in, for example <c>fr-FR</c>, so the guardian can show its crash window in that language;
/// <see langword="null"/> when none is known or the recorded value is not a supported Foundry culture.
/// </param>
internal sealed record UiState(UiPhase Phase, bool DestructiveStarted, string? Culture = null)
{
    /// <summary>Gets the state of a UI process that has not recorded anything yet.</summary>
    internal static UiState Initial { get; } = new(UiPhase.Connect, DestructiveStarted: false);
}

/// <summary>
/// Reads and writes the UI state file, <c>{ "phase": "connect" | "system" | "deploy", "destructiveStarted": bool,
/// "culture": string }</c> (the culture is optional), whose path the guardian gives the UI process in <see cref="PathEnvironmentVariable"/>.
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
    private const string CultureProperty = "culture";
    private const int MaximumCultureLength = 85;
    private static readonly SupportedCultureCatalog SupportedCultures = FoundrySupportedCultures.CreateCatalog();

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

        var document = new Dictionary<string, object>
        {
            [PhaseProperty] = ToName(state.Phase),
            [DestructiveStartedProperty] = state.DestructiveStarted
        };
        if (state.Culture is not null)
        {
            document[CultureProperty] = state.Culture;
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(document);
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

            return new UiState(parsedPhase, destructive.GetBoolean(), ReadCulture(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // A missing, malformed or unsupported culture is dropped, never a reason to distrust the rest of the state.
    private static string? ReadCulture(JsonElement root)
    {
        if (!root.TryGetProperty(CultureProperty, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return TryGetSupportedCulture(element.GetString())?.Name;
    }

    /// <summary>
    /// Returns the Foundry culture matching <paramref name="name"/> (case-insensitive, at most 85 characters), or
    /// <see langword="null"/> when it is missing, malformed, unknown to Windows or not one of the cultures Foundry offers.
    /// </summary>
    internal static CultureInfo? TryGetSupportedCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumCultureLength)
        {
            return null;
        }

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(name);
            // ValidateOrDefault answers the default culture for an unsupported one, which only a matching name accepts.
            string supported = SupportedCultures.ValidateOrDefault(culture.Name);
            return string.Equals(supported, culture.Name, StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo(supported)
                : null;
        }
        catch (CultureNotFoundException)
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
