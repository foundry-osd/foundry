// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Services.Catalog;
using Foundry.Deploy.Services.Catalog;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentCatalogLoadServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_IsolatesOptionalDriverFailure(bool required)
    {
        var service = new DeploymentCatalogLoadService((id, _) => id == VerifiedCatalogSources.OperatingSystems
            ? Task.FromResult(CatalogSnapshotStoreTests.Document())
            : Task.FromException<VerifiedCatalogDocument>(new IOException("private endpoint details")));
        var result = await service.LoadAsync(new(false, required));
        Assert.NotNull(result.OperatingSystemSnapshot);
        Assert.NotNull(result.DriverPackFailure);
        Assert.DoesNotContain("private", result.DriverPackFailure);
        Assert.Equal(!required, result.CanContinue);
    }

    [Fact]
    public async Task LoadAsync_OfflineDoesNotInvokeAcquisition()
    {
        int calls = 0;
        var service = new DeploymentCatalogLoadService((_, _) => { calls++; throw new InvalidOperationException(); });
        var result = await service.LoadAsync(new(true, false));
        Assert.False(result.CanContinue);
        Assert.Equal(0, calls);
    }
}
