// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment;
namespace Foundry.Deploy.Tests;

public sealed class DeploymentRecoveryJournalTests
{
    [Fact]
    public void MissingJournal_AllowsReadWithoutCreatingDirectory()
    {
        using var workspace = new Workspace();
        Assert.Null(new DeploymentRecoveryJournal(workspace.Path).Read());
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, "State")));
    }

    [Fact]
    public void Write_RoundTripsAndNeverClearsOrReplacesPriorEvidence()
    {
        using var workspace = new Workspace();
        var journal = new DeploymentRecoveryJournal(workspace.Path);
        var expected = Diagnostic();
        journal.Write(expected);
        Assert.Equal(expected, new DeploymentRecoveryJournal(workspace.Path).Read());
        journal.Write(expected);
        Assert.Throws<IOException>(() => journal.Write(expected with { ResourcePath = @"HKLM\FoundryOther" }));
        Assert.Equal(expected, journal.Read());
        Assert.Empty(Directory.GetFiles(Path.Combine(workspace.Path, "State"), "*.tmp"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"State\":\"RecoveryRequired\",\"State\":\"NotMounted\"}")]
    public void Read_MalformedExistingRecordBlocksWithoutChangingIt(string contents)
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "State"));
        string file = Path.Combine(workspace.Path, "State", "deployment-recovery.json");
        File.WriteAllText(file, contents);
        var journal = new DeploymentRecoveryJournal(workspace.Path);
        Assert.Throws<IOException>(() => journal.Read());
        Assert.Throws<IOException>(() => journal.Write(Diagnostic()));
        Assert.Equal(contents, File.ReadAllText(file));
    }

    [Fact]
    public void Read_OversizeExistingRecordBlocks()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "State"));
        File.WriteAllText(Path.Combine(workspace.Path, "State", "deployment-recovery.json"), new string('x', 16 * 1024 + 1));
        Assert.Throws<IOException>(() => new DeploymentRecoveryJournal(workspace.Path).Read());
    }

    [Fact]
    public void Path_RejectsReparseAncestorWithoutFollowingIt()
    {
        using var workspace = new Workspace();
        string state = Path.Combine(workspace.Path, "State");
        Assert.Throws<IOException>(() => DeploymentRecoveryJournal.ValidatePath(Path.Combine(state, "deployment-recovery.json"),
            path => path == state ? FileAttributes.ReparsePoint | FileAttributes.Directory : FileAttributes.Normal));
    }

    private static RecoveryResourceDiagnostic Diagnostic() => new(RecoveryResourceState.RecoveryRequired, "RegistryHive", @"HKLM\FoundryOwned", @"C:\owned\SYSTEM", "hive_cleanup_unresolved");
    private sealed class Workspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry-Journal-Test-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
