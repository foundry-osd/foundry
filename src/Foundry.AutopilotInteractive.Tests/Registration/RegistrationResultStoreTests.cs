// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.AutopilotInteractive.Services.Registration;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class RegistrationResultStoreTests : IDisposable
{
    private static readonly DateTimeOffset Completed = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "foundry-result-" + Guid.NewGuid().ToString("N"));
    private string StateDirectory => Path.Combine(_root, "State");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("failed")]
    public void IsCompleted_IsFalse_ForSkippedOrFailed(string status)
    {
        RegistrationResultStore store = new(StateDirectory);
        store.Write(new RegistrationResult(status, "deadline_reached", null, null, null, Completed));

        Assert.False(store.IsCompleted());
    }

    [Fact]
    public void IsCompleted_IsFalse_ForMissingFile()
    {
        Assert.False(new RegistrationResultStore(StateDirectory).IsCompleted());
    }

    [Fact]
    public void IsCompleted_IsTrue_ForCompleted()
    {
        RegistrationResultStore store = new(StateDirectory);
        store.Write(new RegistrationResult("completed", null, "import", "device", "assigned", Completed));

        Assert.True(store.IsCompleted());
        Assert.Empty(Directory.GetFiles(StateDirectory, "*.tmp"));
    }

    [Fact]
    public void IsCompleted_IsFalse_ForCorruptFile()
    {
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllText(Path.Combine(StateDirectory, "result.json"), "{ not json");

        Assert.False(new RegistrationResultStore(StateDirectory).IsCompleted());
    }

    [Fact]
    public void Write_OverwritesAnEarlierResult()
    {
        RegistrationResultStore store = new(StateDirectory);
        store.Write(new RegistrationResult("failed", "network_unavailable", null, null, null, Completed));
        store.Write(new RegistrationResult("completed", null, "import", "device", "timedOut", Completed));

        Assert.True(store.IsCompleted());
    }

    [Fact]
    public void ResolveStateDirectory_UsesTheWindowsFolderInProductionAndTempInDebug()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string production = RegistrationResultStore.ResolveStateDirectory(debugSafetyMode: false);
        string debug = RegistrationResultStore.ResolveStateDirectory(debugSafetyMode: true);

        Assert.Equal(Path.Combine(windows, "Temp", "Foundry", "State", "AutopilotInteractive"), production, ignoreCase: true);
        Assert.StartsWith(Path.GetTempPath(), debug, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("State", "AutopilotInteractive"), debug, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_ContainsNoGroupTagOrUser()
    {
        RegistrationResultStore store = new(StateDirectory);
        store.Write(new RegistrationResult("completed", null, "import", "device", "assigned", Completed));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(StateDirectory, "result.json")));
        string[] names = [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

        Assert.Equal(["status", "importId", "autopilotDeviceId", "profileAssignment", "completedAtUtc"], names);
        Assert.DoesNotContain(names, name => name.Contains("groupTag", StringComparison.OrdinalIgnoreCase)
            || name.Contains("user", StringComparison.OrdinalIgnoreCase)
            || name.Contains("serial", StringComparison.OrdinalIgnoreCase));
    }
}
