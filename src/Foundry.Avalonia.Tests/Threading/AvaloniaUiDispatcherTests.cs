// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Headless.XUnit;
using Foundry.Avalonia.Services.Threading;

namespace Foundry.Avalonia.Tests.Threading;

public sealed class AvaloniaUiDispatcherTests
{
    [AvaloniaFact]
    public void CheckAccessReturnsTrueOnTheUiThread()
    {
        var dispatcher = new AvaloniaUiDispatcher();

        Assert.True(dispatcher.CheckAccess());
    }

    [AvaloniaFact]
    public async Task PostSchedulesWorkOnTheUiThread()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.Post(() => completion.SetResult(dispatcher.CheckAccess()));

        Assert.True(await completion.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [AvaloniaFact]
    public async Task InvokeAsyncRunsWorkOnTheUiThread()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        bool hasAccess = false;

        await dispatcher.InvokeAsync(() => hasAccess = dispatcher.CheckAccess());

        Assert.True(hasAccess);
    }

    [AvaloniaFact]
    public async Task InvokeAsyncPropagatesConsumerExceptions()
    {
        var dispatcher = new AvaloniaUiDispatcher();

        Task invocation = dispatcher.InvokeAsync(() => throw new InvalidOperationException("Consumer failure."));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => invocation);
        Assert.Equal("Consumer failure.", exception.Message);
    }
}
