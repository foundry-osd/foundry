// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.ViewModels;
using Foundry.Wpf.Progress;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Smoke test of the shared progress page hosted with Deploy's resources: every resource resolves and every binding finds its
/// source in each debug preview state.
/// </summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class ProgressPageViewBindingTests
{
    [Theory]
    [InlineData("Progress")]
    [InlineData("Success")]
    [InlineData("Error")]
    [InlineData("Cancelled")]
    public Task View_InDebugPreviewState_ResolvesResourcesAndBindings(string state) => WpfApplicationTestHost.RunAsync(async () =>
    {
        var listener = new RecordingTraceListener();
        PresentationTraceSources.Refresh();
        SourceLevels previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            using DeploymentSessionViewModel session = DeploymentSessionDebugPreviewTests.CreateSession(Dispatcher.CurrentDispatcher);
            ShowPreview(session, state);

            // A missing resource or a parse error throws from the constructor.
            var view = new ProgressPageView { DataContext = session };
            await RenderAsync(view);

            Assert.Empty(listener.Messages);
            Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);

            // The listener must observe a broken binding, so an empty record means the page bound cleanly.
            var probe = new TextBlock { DataContext = session };
            probe.SetBinding(TextBlock.TextProperty, new Binding("MissingProperty"));
            await RenderAsync(probe);
            Assert.NotEmpty(listener.Messages);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }
    });

    private static void ShowPreview(DeploymentSessionViewModel session, string state)
    {
        DeploymentContext request = DeploymentSessionDebugPreviewTests.Request();
        switch (state)
        {
            case "Progress":
                session.ShowDebugProgress(request);
                break;
            case "Success":
                session.ShowDebugSuccess(request);
                break;
            case "Error":
                session.ShowDebugError(request, "Image preview failed.");
                break;
            default:
                session.ShowDebugCancelled(request);
                break;
        }
    }

    private static async Task RenderAsync(FrameworkElement element)
    {
        element.Measure(new Size(1280, 800));
        element.Arrange(new Rect(0, 0, 1280, 800));
        element.UpdateLayout();
        // Deferred binding transfers and the page's dispatcher callbacks run before control returns.
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message) => Messages.Enqueue(message ?? string.Empty);
    }
}
