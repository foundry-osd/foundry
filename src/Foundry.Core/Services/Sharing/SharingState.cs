// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// The sharing role of this PC, persisted in <c>Configuration\sharing.json</c> by <see cref="ConfigurationSharingEngine"/>. It holds
/// no secret: passwords and keys live in the vault under the sharing ids of <see cref="Secrets.SecretIds"/>.
/// </summary>
public sealed record SharingState
{
    /// <summary>Gets the role of this PC.</summary>
    public SharingRole Role { get; init; }

    /// <summary>Gets the publication folder this PC publishes to or follows; null without a role.</summary>
    public string? Folder { get; init; }

    /// <summary>
    /// Gets the publisher id: this PC's own on a reference (kept when publishing stops), the followed reference's on a follower.
    /// </summary>
    public Guid PublisherId { get; init; }

    /// <summary>Gets the display name of the publisher: this PC on a reference, the followed reference on a follower.</summary>
    public string? PublisherName { get; init; }

    /// <summary>Gets whether passwords are published (reference) or the applied publication includes them (follower).</summary>
    public bool IncludePasswords { get; init; }

    /// <summary>Gets the pinned reference public key (DER SubjectPublicKeyInfo) of a follower.</summary>
    public byte[]? PinnedPublicKey { get; init; }

    /// <summary>Gets the sequence a follower applied last; 0 when none.</summary>
    public long AppliedSequence { get; init; }

    /// <summary>
    /// Gets the sequence of the last successful publish of this PC. It is persisted after every publish and passed in every
    /// <see cref="PublicationRequest"/>, so a folder writer who moves <c>current.json</c> back cannot make the next version reuse
    /// an older sequence. Kept when publishing stops.
    /// </summary>
    public long LastPublishedSequence { get; init; }
}

/// <summary>
/// A new reference a follower found: the current manifest is signed by another key than the pinned one, and it opens with the
/// publication password and verifies under the publisher key it embeds. Shown with the pinned key id for "Accept new reference".
/// </summary>
/// <param name="KeyId">Fingerprint of <paramref name="PublicKey"/>.</param>
/// <param name="PublicKey">The new reference public key (DER SubjectPublicKeyInfo), pinned on acceptance.</param>
/// <param name="Name">Display name of the new reference PC.</param>
/// <param name="Sequence">Sequence of the manifest it signed.</param>
public sealed record SharingPublisherCandidate(string KeyId, byte[] PublicKey, string Name, long Sequence);

/// <summary>Reads and writes <see cref="SharingState"/> as JSON, replacing the file atomically.</summary>
internal static class SharingStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Reads the state; a missing file is a PC without a role, and a malformed one is logged and read the same way.</summary>
    /// <exception cref="IOException">The file exists but cannot be read, for example because it is locked.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file is denied.</exception>
    public static SharingState Load(string path)
    {
        if (!File.Exists(path))
        {
            return new SharingState();
        }

        try
        {
            return JsonSerializer.Deserialize<SharingState>(File.ReadAllBytes(path), SerializerOptions) ?? new SharingState();
        }
        catch (JsonException)
        {
            Log.ForContext<ConfigurationSharingEngine>().Warning("The configuration sharing state is malformed; this PC starts without a sharing role.");
            return new SharingState();
        }
    }

    /// <summary>Writes the state to a uniquely named temporary file, flushes it to disk and renames it over <paramref name="path"/>.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the location is denied.</exception>
    public static void Save(string path, SharingState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
