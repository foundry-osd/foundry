// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration.Legacy;

public sealed class LegacyMigrationStateFileTests : IDisposable
{
    private readonly TemporaryDirectory temp = new();

    private string StatePath => Path.Combine(temp.Path, "Configuration", "migration.json");

    [Fact]
    public void Load_MissingFile_ReturnsNotCompleted()
    {
        LegacyMigrationState state = new LegacyMigrationStateFile(StatePath).Load();

        Assert.False(state.ConfigurationSharingCompleted);
        Assert.Empty(state.PendingNotices);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PersistsNoticesByName()
    {
        var file = new LegacyMigrationStateFile(StatePath);

        file.Save(new LegacyMigrationState
        {
            ConfigurationSharingCompleted = true,
            PendingNotices = [MigrationNotice.PasswordsNeeded, MigrationNotice.FilesToReselect]
        });
        LegacyMigrationState loaded = file.Load();

        Assert.True(loaded.ConfigurationSharingCompleted);
        Assert.Equal([MigrationNotice.PasswordsNeeded, MigrationNotice.FilesToReselect], loaded.PendingNotices);
        Assert.Contains("\"FilesToReselect\"", File.ReadAllText(StatePath), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(StatePath)!));
    }

    [Fact]
    public void Load_DamagedFile_ReadsAsCompletedWithoutNotices()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, "{ \"configurationSharingCompleted\": ");

        LegacyMigrationState state = new LegacyMigrationStateFile(StatePath).Load();

        Assert.True(state.ConfigurationSharingCompleted);
        Assert.Empty(state.PendingNotices);
    }

    public void Dispose() => temp.Dispose();
}
