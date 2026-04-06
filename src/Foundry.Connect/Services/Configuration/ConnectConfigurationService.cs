using System.IO;
using System.Text.Json;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Runtime;

namespace Foundry.Connect.Services.Configuration;

public sealed class ConnectConfigurationService : IConnectConfigurationService
{
    private const string DefaultConfigFileName = "foundry.connect.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string[] _args;

    public ConnectConfigurationService(string[] args)
    {
        _args = args ?? Array.Empty<string>();
    }

    public string? ConfigurationPath { get; private set; }

    public bool IsLoadedFromDisk { get; private set; }

    public FoundryConnectConfiguration Load()
    {
        ConfigurationResolution resolution = ResolveConfigurationPath(_args);
        ConfigurationPath = resolution.Path;
        if (string.IsNullOrWhiteSpace(ConfigurationPath))
        {
            IsLoadedFromDisk = false;
            return Normalize(new FoundryConnectConfiguration());
        }

        string fullPath = Path.GetFullPath(ConfigurationPath);
        if (!File.Exists(fullPath))
        {
            if (!resolution.IsRequired)
            {
                ConfigurationPath = null;
                IsLoadedFromDisk = false;
                return Normalize(new FoundryConnectConfiguration());
            }

            throw new FoundryConnectConfigurationException($"Configuration file was not found: {fullPath}");
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            FoundryConnectConfiguration? configuration = JsonSerializer.Deserialize<FoundryConnectConfiguration>(json, JsonOptions);
            if (configuration is null)
            {
                throw new FoundryConnectConfigurationException($"Configuration file is empty or invalid: {fullPath}");
            }

            ConfigurationPath = fullPath;
            IsLoadedFromDisk = true;
            return Normalize(configuration);
        }
        catch (FoundryConnectConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FoundryConnectConfigurationException($"Configuration file could not be parsed: {fullPath}", ex);
        }
    }

    private static ConfigurationResolution ResolveConfigurationPath(IEnumerable<string> args)
    {
        string? envPath = Environment.GetEnvironmentVariable("FOUNDRY_CONNECT_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return new ConfigurationResolution(envPath, IsRequired: true);
        }

        string[] values = args.ToArray();
        for (int index = 0; index < values.Length; index++)
        {
            string value = values[index];
            if (value.Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < values.Length)
            {
                return new ConfigurationResolution(values[index + 1], IsRequired: true);
            }
        }

        return new ConfigurationResolution(
            ConnectWorkspacePaths.GetConfigFilePath(DefaultConfigFileName),
            IsRequired: ConnectWorkspacePaths.IsWinPeRuntime());
    }

    private static FoundryConnectConfiguration Normalize(FoundryConnectConfiguration configuration)
    {
        NetworkCapabilitiesOptions capabilities = configuration.Capabilities ?? new NetworkCapabilitiesOptions();
        InternetProbeOptions probe = configuration.InternetProbe ?? new InternetProbeOptions();

        string[] probeUris = probe.ProbeUris
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(static value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (probeUris.Length == 0)
        {
            probeUris =
            [
                "http://www.msftconnecttest.com/connecttest.txt",
                "http://www.google.com"
            ];
        }

        return new FoundryConnectConfiguration
        {
            SchemaVersion = configuration.SchemaVersion <= 0
                ? FoundryConnectConfiguration.CurrentSchemaVersion
                : configuration.SchemaVersion,
            Capabilities = new NetworkCapabilitiesOptions
            {
                WifiProvisioned = capabilities.WifiProvisioned
            },
            Dot1x = configuration.Dot1x ?? new Dot1xSettings(),
            Wifi = configuration.Wifi ?? new WifiSettings(),
            InternetProbe = new InternetProbeOptions
            {
                ProbeUris = probeUris,
                TimeoutSeconds = Math.Clamp(probe.TimeoutSeconds, 1, 30)
            }
        };
    }

    private readonly record struct ConfigurationResolution(string? Path, bool IsRequired);
}
