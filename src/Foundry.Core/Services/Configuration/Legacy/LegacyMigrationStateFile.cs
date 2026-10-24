// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>The state of the first-launch legacy migration for one Windows user.</summary>
public sealed record LegacyMigrationState
{
    /// <summary>Gets whether the migration from the removed deployment profiles completed; it never runs again.</summary>
    public bool ConfigurationSharingCompleted { get; init; }

    /// <summary>Gets the notices the Home page still shows, until each is dismissed.</summary>
    public IReadOnlyList<MigrationNotice> PendingNotices { get; init; } = [];
}

/// <summary>
/// Stores the <see cref="LegacyMigrationState"/> of the current Windows user in one JSON file, written atomically.
/// </summary>
/// <remarks>
/// The legacy profiles, the secret vault and the configuration are per user, so the migration state lives beside them rather
/// than in the machine-wide application settings: each Windows user is migrated once and sees only their own notices.
/// </remarks>
public sealed class LegacyMigrationStateFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<MigrationNotice>() }
    };

    private readonly string path;

    /// <summary>Creates a state file stored at <paramref name="path"/>.</summary>
    public LegacyMigrationStateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    /// <summary>Reads the state; a missing file means the migration has not run yet.</summary>
    /// <remarks>A file that cannot be parsed reads as completed without notices, so a damaged state never runs the migration twice.</remarks>
    /// <exception cref="IOException">The file cannot be read, for example because it is locked.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file is denied.</exception>
    public LegacyMigrationState Load()
    {
        if (!File.Exists(path)) return new LegacyMigrationState();
        byte[] json = File.ReadAllBytes(path);
        try
        {
            return JsonSerializer.Deserialize<LegacyMigrationState>(json, Options) ?? new LegacyMigrationState { ConfigurationSharingCompleted = true };
        }
        catch (JsonException)
        {
            return new LegacyMigrationState { ConfigurationSharingCompleted = true };
        }
    }

    /// <summary>Replaces the state by writing a uniquely named temporary file and renaming it over the state file.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the location is denied.</exception>
    public void Save(LegacyMigrationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
