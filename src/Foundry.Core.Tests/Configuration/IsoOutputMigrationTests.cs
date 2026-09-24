// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class IsoOutputMigrationTests
{
    [Theory]
    [InlineData(@"C:\ProgramData\Foundry\Workspaces\Iso\Foundry.iso", true)]
    [InlineData("C:/ProgramData/Foundry/Workspaces/Iso/Foundry.iso", true)]
    [InlineData(@"D:\Custom\Foundry.iso", false)]
    [InlineData(null, false)]
    public void MigrateDefaultIsoOutput_ChangesOnlyTheRecognizedDefault(string? output, bool migrate)
    {
        const string oldPath = @"C:\ProgramData\Foundry\Workspaces\Iso\Foundry.iso";
        const string newPath = @"C:\ProgramData\Foundry\Artifacts\Iso\Foundry.iso";
        var document = new FoundryConfigurationDocument { General = new() { IsoOutputPath = output } };

        FoundryConfigurationDocument result = FoundryConfigurationMigration.MigrateDefaultIsoOutput(document, oldPath, newPath);

        Assert.Equal(migrate ? newPath : output, result.General.IsoOutputPath);
        Assert.Equal(result, FoundryConfigurationMigration.MigrateDefaultIsoOutput(result, oldPath, newPath));
    }
}
