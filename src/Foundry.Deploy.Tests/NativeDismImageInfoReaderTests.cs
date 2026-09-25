// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using NativeMetadata = Foundry.Utilities.Imaging.WindowsImageMetadata;

namespace Foundry.Deploy.Tests;

public sealed class NativeDismImageInfoReaderTests
{
    [Fact]
    public async Task ReadAsync_PreservesUnsignedSizeIndexVersionAndUnknownArchitecture()
    {
        var expected = new NativeMetadata(7, "Custom Windows", "AnyEdition", ulong.MaxValue, "unknown", new Version(10, 0, 26100, 4652), "Description", "WinNT", [], 0);
        string? observedPath = null;
        CancellationToken observedToken = default;
        var reader = new NativeDismImageInfoReader((path, token) =>
        {
            observedPath = path;
            observedToken = token;
            return Task.FromResult<IReadOnlyList<NativeMetadata>>([expected]);
        });

        WindowsImageInfo result = Assert.Single(await reader.ReadAsync("install.wim", TestContext.Current.CancellationToken));

        Assert.Equal(new WindowsImageInfo(7, expected.Name, expected.EditionId, ulong.MaxValue, "unknown", expected.Version), result);
        Assert.Equal("install.wim", observedPath);
        Assert.Equal(TestContext.Current.CancellationToken, observedToken);
    }
}
