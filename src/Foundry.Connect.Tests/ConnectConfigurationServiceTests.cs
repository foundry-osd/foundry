using System.Text.Json;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Configuration;
using Foundry.Core.Services.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using CoreConfiguration = Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ConnectConfigurationServiceTests
{
    [Fact]
    public void Load_WhenNoConfigurationFileIsAvailable_ReturnsNormalizedDefaults()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        var service = new ConnectConfigurationService([], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.False(service.IsLoadedFromDisk);
        Assert.Null(service.ConfigurationPath);
        Assert.Equal(FoundryConnectConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(5, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["http://www.msftconnecttest.com/connecttest.txt", "http://www.google.com"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_WhenConfigurationFileContainsMixedProbeUris_NormalizesValues()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "normalized.json",
            """
            {
              "schemaVersion": 0,
              "internetProbe": {
                "probeUris": [
                  " https://example.com/health ",
                  "invalid-uri",
                  "https://example.com/health",
                  "http://contoso.test/connect"
                ],
                "timeoutSeconds": 99
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(System.IO.Path.GetFullPath(configurationPath), service.ConfigurationPath);
        Assert.Equal(FoundryConnectConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(30, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["https://example.com/health", "http://contoso.test/connect"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_WhenEnvironmentVariableIsSet_TakesPrecedenceOverCommandLineArgument()
    {
        using var tempDirectory = new TemporaryDirectory();
        string environmentConfigurationPath = CreateJsonFile(tempDirectory.Path, "environment.json", """{ "schemaVersion": 5 }""");
        string commandLineConfigurationPath = CreateJsonFile(tempDirectory.Path, "argument.json", """{ "schemaVersion": 2 }""");
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", environmentConfigurationPath);

        var service = new ConnectConfigurationService(["--config", commandLineConfigurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(System.IO.Path.GetFullPath(environmentConfigurationPath), service.ConfigurationPath);
        Assert.Equal(5, configuration.SchemaVersion);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationIsProvided_PreservesEffectiveNetworkPaths()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string wiredProfilePath = CreateFile(tempDirectory.Path, "wired.xml", "<LANProfile />");
        string configurationJson = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            new CoreConfiguration.FoundryExpertConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    Dot1x = new CoreConfiguration.Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplatePath = wiredProfilePath
                    }
                }
            },
            tempDirectory.Path).ConfigurationJson;
        string configurationPath = CreateJsonFile(tempDirectory.Path, "core-generated.json", configurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(CoreConfiguration.FoundryConnectConfigurationDocument.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(@"Network\Wired\Profiles\wired.xml", configuration.Dot1x.ProfileTemplatePath);
        Assert.NotNull(configuration.Capabilities);
        Assert.NotNull(configuration.Wifi);
        Assert.NotNull(configuration.InternetProbe);
    }

    private static string CreateJsonFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        using JsonDocument document = JsonDocument.Parse(contents);
        File.WriteAllText(filePath, document.RootElement.GetRawText());
        return filePath;
    }

    private static string CreateFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
