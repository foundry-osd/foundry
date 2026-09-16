// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

internal sealed class StubWindowsImageInfoReader(params WindowsImageInfo[] images) : IWindowsImageInfoReader
{
    public Func<string, IReadOnlyList<WindowsImageInfo>>? Read { get; init; }

    public Task<IReadOnlyList<WindowsImageInfo>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read is null ? (IReadOnlyList<WindowsImageInfo>)images : Read(imagePath));
    }
}
