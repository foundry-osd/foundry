// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Startup;

/// <summary>
/// Loads the runtime configuration Foundry Deploy staged next to the application.
/// </summary>
public static class AutopilotInteractiveConfigurationLoader
{
    /// <summary>
    /// Reads and validates the configuration file, or returns the in-memory debug configuration when no path was given
    /// and <paramref name="allowDebugDefault"/> is set.
    /// </summary>
    /// <param name="configPath">Value of <c>--config</c>, or <see langword="null"/> when absent.</param>
    /// <param name="allowDebugDefault">
    /// Whether a missing <paramref name="configPath"/> yields a debug configuration (culture en-US, theme System, no computer
    /// name). Only a debug session may set it: a shipped build must fail instead of running without its configuration.
    /// </param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidDataException">
    /// No path was given, the file cannot be read, or its content is not a valid configuration.
    /// </exception>
    public static AutopilotInteractiveRuntimeConfiguration Load(string? configPath, bool allowDebugDefault)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return allowDebugDefault
                ? CreateDebugConfiguration()
                : throw new InvalidDataException("No configuration path was supplied (--config).");
        }

        string json;
        try
        {
            json = File.ReadAllText(configPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The Autopilot interactive configuration file could not be read.", exception);
        }

        return AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(json);
    }

    private static AutopilotInteractiveRuntimeConfiguration CreateDebugConfiguration()
    {
        return new AutopilotInteractiveRuntimeConfiguration { CultureName = "en-US" };
    }
}
