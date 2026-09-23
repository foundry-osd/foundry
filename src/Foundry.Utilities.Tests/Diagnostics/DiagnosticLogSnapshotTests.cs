// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class DiagnosticLogSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Foundry.LogSnapshot.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CopyAsync_PrunesOnlyUnchangedOwnedRotations_AndExcludesOutbox()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(source, "PendingLogs"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "Foundry.log"), "active");
        File.WriteAllText(Path.Combine(source, "Foundry_001.log"), "old rotation");
        File.WriteAllText(Path.Combine(source, "Foundry_002.log"), "another rotation");
        File.WriteAllText(Path.Combine(source, "PendingLogs", "private.log"), "outbox");
        File.WriteAllText(Path.Combine(target, "unrelated.log"), "keep");

        Assert.Equal(3, await DiagnosticLogSnapshot.CopyAsync(source, target, "*.log", TestContext.Current.CancellationToken));
        File.Delete(Path.Combine(source, "Foundry_001.log"));
        File.Delete(Path.Combine(source, "Foundry_002.log"));
        File.WriteAllText(Path.Combine(target, "Foundry_002.log"), "external change");

        Assert.Equal(1, await DiagnosticLogSnapshot.CopyAsync(source, target, "*.log", TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(target, "Foundry_001.log")));
        Assert.Equal("external change", File.ReadAllText(Path.Combine(target, "Foundry_002.log")));
        Assert.True(File.Exists(Path.Combine(target, "unrelated.log")));
        Assert.False(Directory.Exists(Path.Combine(target, "PendingLogs")));
    }

    [Fact]
    public async Task CopyAsync_WhenCurrentLogCannotBeRead_DoesNotPrunePriorEvidence()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        string active = Path.Combine(source, "Foundry.log");
        string rotated = Path.Combine(source, "Foundry_001.log");
        File.WriteAllText(active, "active");
        File.WriteAllText(rotated, "retain on failed refresh");
        await DiagnosticLogSnapshot.CopyAsync(source, target, "*.log", TestContext.Current.CancellationToken);
        File.Delete(rotated);
        using FileStream inaccessible = new(active, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => DiagnosticLogSnapshot.CopyAsync(source, target, "*.log", TestContext.Current.CancellationToken));

        Assert.Equal("retain on failed refresh", File.ReadAllText(Path.Combine(target, "Foundry_001.log")));
        Assert.Equal("active", File.ReadAllText(Path.Combine(target, "Foundry.log")));
        Assert.Empty(Directory.GetFiles(target, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
