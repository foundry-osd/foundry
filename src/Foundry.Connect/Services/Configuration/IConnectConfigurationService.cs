using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Configuration;

/// <summary>
/// Loads and normalizes the Foundry.Connect runtime configuration.
/// </summary>
public interface IConnectConfigurationService
{
    /// <summary>
    /// Gets the resolved configuration file path when one was used.
    /// </summary>
    string? ConfigurationPath { get; }

    /// <summary>
    /// Gets whether configuration was loaded from disk instead of built-in defaults.
    /// </summary>
    bool IsLoadedFromDisk { get; }

    /// <summary>
    /// Loads configuration from command-line, environment, or WinPE media locations.
    /// </summary>
    /// <returns>The normalized runtime configuration.</returns>
    FoundryConnectConfiguration Load();
}
