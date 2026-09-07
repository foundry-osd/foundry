// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Text.Json;
using Foundry.Core.Services.Media;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Media;

public sealed class MediaOperationLeaseTests
{
    [Fact]
    public void Acquire_ExcludesConcurrentMediaAndAdkLeases()
    {
        using var temporary = new TemporaryDirectory();
        using (MediaOperationLease first = MediaOperationLease.Acquire(temporary.Path))
        {
            Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path, "ADK"));
            Assert.True(Directory.Exists(first.WorkingDirectoryPath));
            first.DeleteOwnedWorkspace();
        }
        using MediaOperationLease second = MediaOperationLease.Acquire(temporary.Path, "ADK");
        second.DeleteOwnedWorkspace();
    }

    [Fact]
    public void DeleteOwnedWorkspace_PreservesUnrelatedDirectoriesAndContents()
    {
        using var temporary = new TemporaryDirectory();
        string unrelated = Path.Combine(temporary.Path, "existing-project");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "existing");
        using MediaOperationLease lease = MediaOperationLease.Acquire(temporary.Path);
        string nested = Path.Combine(lease.WorkingDirectoryPath, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "owned.txt"), "temporary");
        lease.DeleteOwnedWorkspace();
        Assert.False(Directory.Exists(lease.WorkingDirectoryPath));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(unrelated, "keep.txt")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("incomplete crash journal")]
    public void Acquire_AbandonedJournalBlocksWithoutDeletingEvidence(string journal)
    {
        using var temporary = new TemporaryDirectory();
        string path = Path.Combine(temporary.Path, "abandoned.operation.json");
        File.WriteAllText(path, journal);
        Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path));
        Assert.Equal(journal, File.ReadAllText(path));
        Assert.Empty(Directory.GetDirectories(temporary.Path));
    }

    [Fact]
    public void RetainForRecovery_PreservesWorkspaceJournalAndExclusionAfterDispose()
    {
        using var temporary = new TemporaryDirectory();
        MediaOperationLease lease = MediaOperationLease.Acquire(temporary.Path);
        try
        {
            string evidence = Path.Combine(lease.WorkingDirectoryPath, "staged.iso");
            File.WriteAllText(evidence, "recoverable");
            lease.RetainForRecovery([evidence]);
            lease.Dispose();
            Assert.Throws<InvalidOperationException>(lease.DeleteOwnedWorkspace);
            Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path));
            Assert.Equal("recoverable", File.ReadAllText(evidence));
            using JsonDocument journal = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(temporary.Path, "*.operation.json"))));
            Assert.Equal("RecoveryRequired", journal.RootElement.GetProperty("State").GetString());
            Assert.Equal(evidence, journal.RootElement.GetProperty("RetainedPaths")[0].GetString());
        }
        finally
        {
            ReleaseRetainedTestLeases(temporary.Path);
        }
    }

    [Fact]
    public void Acquire_ProcessExitBeforeCleanupLeavesBlockingOwnershipJournal()
    {
        using var temporary = new TemporaryDirectory();
        using MediaOperationLease abandoned = MediaOperationLease.Acquire(temporary.Path);
        ((FileStream)typeof(MediaOperationLease).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(abandoned)!).Dispose();
        Assert.Throws<IOException>(() => MediaOperationLease.Acquire(temporary.Path));
        Assert.True(Directory.Exists(abandoned.WorkingDirectoryPath));
        Assert.Single(Directory.GetFiles(temporary.Path, "*.operation.json"));
    }
    [Fact]
    public void DeleteOwnedWorkspace_RemovesReadOnlyOwnedFileWithoutChangingUnrelatedFile()
    {
        using var temporary = new TemporaryDirectory();
        using MediaOperationLease lease = MediaOperationLease.Acquire(temporary.Path);
        string owned = Path.Combine(lease.WorkingDirectoryPath, "copied-adk-file.bin");
        string unrelated = Path.Combine(temporary.Path, "unrelated.bin");
        File.WriteAllText(owned, "owned");
        File.WriteAllText(unrelated, "unrelated");
        File.SetAttributes(owned, FileAttributes.ReadOnly);
        File.SetAttributes(unrelated, FileAttributes.ReadOnly);
        try
        {
            lease.DeleteOwnedWorkspace();
            Assert.False(Directory.Exists(lease.WorkingDirectoryPath));
            Assert.Equal("unrelated", File.ReadAllText(unrelated));
            Assert.True((File.GetAttributes(unrelated) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(owned)) File.SetAttributes(owned, FileAttributes.Normal);
            File.SetAttributes(unrelated, FileAttributes.Normal);
        }
    }
    // Simulates process exit for test-owned recovery leases without adding a production release bypass.
    internal static void ReleaseRetainedTestLeases(string ownedRoot)
    {
        var leases = (List<MediaOperationLease>)typeof(MediaOperationLease)
            .GetField("retainedLeases", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (leases)
        {
            foreach (MediaOperationLease lease in leases.Where(lease =>
                Path.GetDirectoryName(lease.WorkingDirectoryPath) == ownedRoot).ToArray())
            {
                ((FileStream)typeof(MediaOperationLease).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(lease)!).Dispose();
                leases.Remove(lease);
            }
        }
    }
}
