// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Runs test code on one process-wide STA thread that owns Deploy's <see cref="App"/>, initialized from App.xaml as
/// <c>Program</c> does, so tests see the resources the Foundry.WinPE host loads in Deploy mode.
/// </summary>
/// <remarks>
/// WPF allows one <see cref="Application"/> per process, and view models capture <c>Application.Current.Dispatcher</c>, so the
/// thread keeps pumping for the lifetime of the test process instead of being shut down after a test. Tests that use the host
/// belong to <see cref="WpfApplicationHostCollection"/>.
/// </remarks>
internal static class WpfApplicationTestHost
{
    private static readonly Lazy<Dispatcher> HostDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the exceptions that reached the host dispatcher without being observed by a test.</summary>
    public static ConcurrentQueue<Exception> UnhandledExceptions { get; } = new();

    /// <summary>
    /// Runs <paramref name="test"/> on the host thread, where awaits resume on the host dispatcher. The unhandled exception
    /// queue is cleared first, so a test only observes the exceptions raised while it runs.
    /// </summary>
    public static Task RunAsync(Func<Task> test) => HostDispatcher.Value.InvokeAsync(() =>
    {
        UnhandledExceptions.Clear();
        return test();
    }).Task.Unwrap();

    private static Dispatcher Start()
    {
        TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) =>
                {
                    UnhandledExceptions.Enqueue(e.Exception);
                    e.Handled = true;
                };
                App application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.InitializeComponent();
                ready.SetResult(dispatcher);
            }
            catch (Exception exception)
            {
                ready.SetException(exception);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test application"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}

/// <summary>Serializes the tests that share <see cref="WpfApplicationTestHost"/> and the process-wide binding trace source.</summary>
[CollectionDefinition(nameof(WpfApplicationHostCollection), DisableParallelization = true)]
public sealed class WpfApplicationHostCollection;
