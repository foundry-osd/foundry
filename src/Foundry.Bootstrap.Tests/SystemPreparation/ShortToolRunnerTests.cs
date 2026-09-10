// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.SystemPreparation;
using Xunit;

namespace Foundry.Bootstrap.Tests.SystemPreparation;

public sealed class ShortToolRunnerTests
{
    [Fact]
    public async Task LargeOutputOnBothStreamsDoesNotBlockNonzeroExit()
    {
        var runner = new ShortToolRunner();
        const string command = "(for /l %i in (1,1,4096) do @(echo 0123456789012345678901234567890123456789012345678901234567890123 & echo 0123456789012345678901234567890123456789012345678901234567890123 1>&2)) & exit /b 23";

        int exitCode = await runner.RunAsync(
            Environment.GetEnvironmentVariable("ComSpec")!,
            ["/d", "/c", command],
            TimeSpan.FromSeconds(15),
            TestContext.Current.CancellationToken);

        Assert.Equal(23, exitCode);
    }

    [Fact]
    public async Task AlreadyCancelledRunDoesNotStartMissingExecutable()
    {
        var runner = new ShortToolRunner();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "missing-short-tool.exe",
            [],
            TimeSpan.FromSeconds(5),
            new CancellationToken(true)));
    }
}
