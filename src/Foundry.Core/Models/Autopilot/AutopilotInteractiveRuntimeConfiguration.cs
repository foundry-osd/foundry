// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Models.Autopilot;

/// <summary>
/// Runtime configuration Foundry Deploy stages for <c>Foundry.AutopilotInteractive</c> in the target operating system.
/// </summary>
/// <remarks>
/// Deploy writes this document as <see cref="FileName"/> next to the staged runtime and the application reads it once at
/// startup. It holds no secret: the tenant, client identifier and scopes are compiled-in constants of
/// <see cref="Foundry.Core.Services.Autopilot.AutopilotInteractiveAuthentication"/>.
/// </remarks>
public sealed record AutopilotInteractiveRuntimeConfiguration
{
    /// <summary>Schema version written by this build and the only one the reader accepts.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Name of the configuration file inside the staged runtime directory.</summary>
    public const string FileName = "autopilot-interactive.json";

    /// <summary>Schema version of the document; the reader rejects any value other than <see cref="CurrentSchemaVersion"/>.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Computer name the registration assigns to the device, or <see langword="null"/> when none is configured.</summary>
    public string? AssignedComputerName { get; init; }

    /// <summary>Whether the group tag page opens with the custom group tag entry preselected.</summary>
    public bool PreselectCustomGroupTag { get; init; }

    /// <summary>Whether the registration page offers to assign a user to the device.</summary>
    public bool OfferAssignedUser { get; init; }

    /// <summary>Theme the application starts with, carried over from the previous runtime application.</summary>
    public RuntimeThemePreference Theme { get; init; } = RuntimeThemePreference.System;

    /// <summary>UI culture name, for example <c>fr-FR</c>. Must not be empty.</summary>
    public string CultureName { get; init; } = string.Empty;

    /// <summary>Diagnostic session identifier shared with Bootstrap, Connect and Deploy for log correlation, when known.</summary>
    public string? DiagnosticSessionId { get; init; }
}

/// <summary>
/// Reads and writes <see cref="AutopilotInteractiveRuntimeConfiguration"/> as camelCase JSON with enums as strings.
/// </summary>
public static class AutopilotInteractiveRuntimeConfigurationSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>Serializes the configuration to indented JSON.</summary>
    /// <param name="value">Configuration to serialize.</param>
    /// <returns>The JSON document.</returns>
    public static string Serialize(AutopilotInteractiveRuntimeConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>Parses and validates a configuration document.</summary>
    /// <param name="json">The JSON document.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidDataException">
    /// The JSON is malformed, holds an unknown theme, declares a schema version other than
    /// <see cref="AutopilotInteractiveRuntimeConfiguration.CurrentSchemaVersion"/>, or has an empty culture name.
    /// </exception>
    public static AutopilotInteractiveRuntimeConfiguration Deserialize(string json)
    {
        AutopilotInteractiveRuntimeConfiguration? value;
        try
        {
            value = JsonSerializer.Deserialize<AutopilotInteractiveRuntimeConfiguration>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentNullException or NotSupportedException)
        {
            throw new InvalidDataException("The Autopilot interactive configuration is not valid JSON.", exception);
        }

        if (value is null)
        {
            throw new InvalidDataException("The Autopilot interactive configuration is empty.");
        }

        if (value.SchemaVersion != AutopilotInteractiveRuntimeConfiguration.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"The Autopilot interactive configuration schema version {value.SchemaVersion} is not supported.");
        }

        if (string.IsNullOrWhiteSpace(value.CultureName))
        {
            throw new InvalidDataException("The Autopilot interactive configuration has no culture name.");
        }

        return value;
    }
}
