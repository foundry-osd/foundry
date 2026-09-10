// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.Diagnostics;
using Xunit;

namespace Foundry.Bootstrap.Tests.Diagnostics;

public sealed class BootstrapLogShutdownTests
{
    [Fact]
    public async Task FlushAsync_ContainsSynchronousFailure()
    {
        await BootstrapLogShutdown.FlushAsync(
            () => throw new InvalidOperationException("sink failure"), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FlushAsync_TimesOutWhenCallbackBlocksSynchronously()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Task flush = BootstrapLogShutdown.FlushAsync(() =>
            {
                started.SetResult();
                try { release.Wait(); }
                finally { completed.SetResult(); }
                return Task.CompletedTask;
            }, TimeSpan.FromMilliseconds(50));

            await started.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await flush.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task FlushAsync_TimesOutWhenCallbackDoesNotCompleteAsynchronously()
    {
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await BootstrapLogShutdown.FlushAsync(() => callback.Task, TimeSpan.FromMilliseconds(50))
                .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            callback.SetResult();
            await callback.Task;
        }
    }
}
