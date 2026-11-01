// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>
/// Outcome of the registration step, read by the next start of the application (the completed guard) and kept for
/// diagnostics.
/// </summary>
/// <remarks>
/// Foundry.PostInstall does not read this file: it maps only the process exit code. Deliberately carries no hardware hash,
/// serial number, group tag or user name.
/// </remarks>
/// <param name="Status"><c>completed</c>, <c>failed</c> or <c>skipped</c>.</param>
/// <param name="FailureCode">Stable token from <see cref="RegistrationFailureCodes"/>, or <see langword="null"/>.</param>
/// <param name="ImportId">Graph import identifier, or <see langword="null"/>.</param>
/// <param name="AutopilotDeviceId">Autopilot device identity, or <see langword="null"/>.</param>
/// <param name="ProfileAssignment"><c>assigned</c>, <c>failed</c>, <c>timedOut</c>, <c>skipped</c>, <c>unavailable</c>, or <see langword="null"/> when the wait did not start.</param>
/// <param name="CompletedAtUtc">Instant the result was produced.</param>
public sealed record RegistrationResult(
    string Status,
    string? FailureCode,
    string? ImportId,
    string? AutopilotDeviceId,
    string? ProfileAssignment,
    DateTimeOffset CompletedAtUtc)
{
    /// <summary>Status of a registration that finished.</summary>
    public const string StatusCompleted = "completed";

    /// <summary>Status of a registration that failed.</summary>
    public const string StatusFailed = "failed";

    /// <summary>Status of a registration the technician skipped.</summary>
    public const string StatusSkipped = "skipped";

    /// <summary>
    /// Converts a profile wait outcome to the value stored in <see cref="ProfileAssignment"/>.
    /// </summary>
    /// <param name="outcome">How the profile wait ended.</param>
    /// <returns><c>assigned</c>, <c>failed</c>, <c>timedOut</c>, <c>skipped</c> or <c>unavailable</c>.</returns>
    public static string ToResultValue(ProfileAssignmentOutcome outcome) => outcome switch
    {
        ProfileAssignmentOutcome.Assigned => "assigned",
        ProfileAssignmentOutcome.Failed => "failed",
        ProfileAssignmentOutcome.TimedOut => "timedOut",
        ProfileAssignmentOutcome.Skipped => "skipped",
        ProfileAssignmentOutcome.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}

/// <summary>
/// Reads and writes <c>result.json</c> in the application state directory.
/// </summary>
/// <param name="stateDirectory">Directory holding <see cref="AutopilotInteractiveLayout.ResultFileName"/>.</param>
public sealed class RegistrationResultStore(string stateDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path = Path.Combine(stateDirectory, AutopilotInteractiveLayout.ResultFileName);

    /// <summary>
    /// Resolves the directory that holds the result file.
    /// </summary>
    /// <param name="debugSafetyMode">
    /// <see langword="true"/> in a debug session, which uses a folder under the user's temporary directory so a development
    /// machine never writes into its Windows folder.
    /// </param>
    /// <returns>The state directory under the retained Foundry root, <c>%SystemRoot%\Temp\Foundry</c> in production.</returns>
    public static string ResolveStateDirectory(bool debugSafetyMode)
    {
        string root = debugSafetyMode
            ? Path.Combine(Path.GetTempPath(), "Foundry")
            : Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp\Foundry");
        return Path.GetFullPath(Path.Combine(root, AutopilotInteractiveLayout.StateRelativePath));
    }

    /// <summary>
    /// Tells whether an earlier run already registered the device.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when the file holds the <c>completed</c> status; a missing, unreadable or malformed file
    /// is <see langword="false"/> so a damaged result never blocks a new attempt.
    /// </returns>
    public bool IsCompleted()
    {
        try
        {
            using FileStream stream = File.OpenRead(_path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out JsonElement status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), RegistrationResult.StatusCompleted, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the result, replacing any earlier one.
    /// </summary>
    /// <remarks>
    /// The content goes to a uniquely named temporary file that is flushed to disk and then moved over the target, so a reader
    /// never sees a partial file.
    /// </remarks>
    /// <param name="result">The result to persist.</param>
    public void Write(RegistrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(stateDirectory);
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, SerializerOptions)));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The original failure is the one to surface.
            }

            throw;
        }
    }
}
