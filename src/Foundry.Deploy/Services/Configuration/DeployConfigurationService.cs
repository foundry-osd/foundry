using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Configuration;

public sealed class DeployConfigurationService(
    ILogger<DeployConfigurationService> logger) : IDeployConfigurationService
{
    public const string DefaultConfigurationPath = @"X:\Foundry\Config\foundry.deploy.config.json";

    private readonly ILogger<DeployConfigurationService> _logger = logger;

    public DeployConfigurationLoadResult LoadOptional()
    {
        if (!File.Exists(DefaultConfigurationPath))
        {
            _logger.LogInformation(
                "No deploy configuration was found at '{ConfigurationPath}'.",
                DefaultConfigurationPath);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = DefaultConfigurationPath,
                Exists = false
            };
        }

        try
        {
            using FileStream stream = File.OpenRead(DefaultConfigurationPath);
            FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
                stream,
                ConfigurationJsonDefaults.SerializerOptions);

            if (document is null)
            {
                const string failureMessage = "The configuration file was empty or could not be parsed.";
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' could not be parsed: {FailureMessage}",
                    DefaultConfigurationPath,
                    failureMessage);

                return new DeployConfigurationLoadResult
                {
                    ConfigurationPath = DefaultConfigurationPath,
                    Exists = true,
                    FailureMessage = failureMessage
                };
            }

            if (document.SchemaVersion > FoundryDeployConfigurationDocument.CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' uses schema version {SchemaVersion}, newer than supported schema version {SupportedSchemaVersion}. Unknown properties will be ignored.",
                    DefaultConfigurationPath,
                    document.SchemaVersion,
                    FoundryDeployConfigurationDocument.CurrentSchemaVersion);
            }

            _logger.LogInformation(
                "Loaded deploy configuration from '{ConfigurationPath}' (SchemaVersion={SchemaVersion}).",
                DefaultConfigurationPath,
                document.SchemaVersion);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = DefaultConfigurationPath,
                Exists = true,
                Document = document
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "Failed to load deploy configuration from '{ConfigurationPath}'.",
                DefaultConfigurationPath);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = DefaultConfigurationPath,
                Exists = true,
                FailureMessage = ex.Message
            };
        }
    }
}
