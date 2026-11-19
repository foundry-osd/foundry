// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Tests.Support;

namespace Foundry.Wpf.Tests.Shell;

public sealed class StatusPageModelTests
{
    [Fact]
    public void Status_RaisesPropertyChangedOnlyWhenTheTextChanges()
    {
        var model = new StatusPageModel("Title", "First");
        List<string?> raised = [];
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.Status = "Second";
        model.Status = "Second";

        Assert.Equal("Title", model.Title);
        Assert.Equal("Second", model.Status);
        Assert.Equal([nameof(StatusPageModel.Status)], raised);
    }

    [Fact]
    public async Task EnsureMinimumDisplayAsync_WaitsForTheMinimumWhenTheWorkIsFast()
    {
        var time = new ManualTimeProvider();
        Task display = StatusPageModel.EnsureMinimumDisplayAsync(Task.CompletedTask, TimeSpan.FromSeconds(1), time);

        time.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Yield();
        Assert.False(display.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await display.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EnsureMinimumDisplayAsync_DoesNotWaitMoreWhenTheWorkIsSlower()
    {
        var time = new ManualTimeProvider();
        var work = new TaskCompletionSource();
        Task display = StatusPageModel.EnsureMinimumDisplayAsync(work.Task, TimeSpan.FromSeconds(1), time);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.False(display.IsCompleted);

        work.SetResult();
        await display.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
