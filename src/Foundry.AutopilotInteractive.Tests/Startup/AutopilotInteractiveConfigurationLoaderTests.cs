// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Startup;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Runtime;

namespace Foundry.AutopilotInteractive.Tests.Startup;

public sealed class AutopilotInteractiveConfigurationLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "foundry-ai-" + Guid.NewGuid().ToString("N"));

    public AutopilotInteractiveConfigurationLoaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Load_ReadsTheConfigurationFile()
    {
        string path = Path.Combine(_directory, AutopilotInteractiveRuntimeConfiguration.FileName);
        File.WriteAllText(path, AutopilotInteractiveRuntimeConfigurationSerializer.Serialize(new AutopilotInteractiveRuntimeConfiguration
        {
            CultureName = "fr-FR",
            AssignedComputerName = "PC-001",
            Theme = RuntimeThemePreference.Dark
        }));

        AutopilotInteractiveRuntimeConfiguration configuration = AutopilotInteractiveConfigurationLoader.Load(path, allowDebugDefault: false);

        Assert.Equal("fr-FR", configuration.CultureName);
        Assert.Equal("PC-001", configuration.AssignedComputerName);
        Assert.Equal(RuntimeThemePreference.Dark, configuration.Theme);
    }

    [Fact]
    public void Load_Throws_WhenPathIsMissingOutsideDebug()
    {
        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveConfigurationLoader.Load(null, allowDebugDefault: false));
    }

    [Fact]
    public void Load_UsesDebugConfiguration_WhenPathIsMissingInDebug()
    {
        AutopilotInteractiveRuntimeConfiguration configuration = AutopilotInteractiveConfigurationLoader.Load(null, allowDebugDefault: true);

        Assert.Equal("en-US", configuration.CultureName);
        Assert.Equal(RuntimeThemePreference.System, configuration.Theme);
        Assert.Null(configuration.AssignedComputerName);
    }

    [Fact]
    public void Load_Throws_WhenFileDoesNotExist_EvenInDebug()
    {
        string path = Path.Combine(_directory, "missing.json");

        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveConfigurationLoader.Load(path, allowDebugDefault: true));
    }

    [Fact]
    public void Load_Throws_WhenContentIsInvalid()
    {
        string path = Path.Combine(_directory, "invalid.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveConfigurationLoader.Load(path, allowDebugDefault: false));
    }
}
