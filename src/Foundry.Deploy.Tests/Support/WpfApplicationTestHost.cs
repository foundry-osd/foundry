// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Runs test code on one process-wide STA thread that owns a WPF <see cref="Application"/> whose resources each test project
/// supplies in <c>WpfApplicationTestHost.Resources.cs</c> (<see cref="AddResources"/>): Deploy's phase resources here, the
/// UI process's complete resources in Foundry.WinPE.Tests, which compiles this file as a link.
/// </summary>
/// <remarks>
/// WPF allows one <see cref="Application"/> per process, and view models capture <c>Application.Current.Dispatcher</c>, so the
/// thread keeps pumping for the lifetime of the test process instead of being shut down after a test. Tests that use the host
/// belong to <see cref="WpfApplicationHostCollection"/>.
/// </remarks>
internal static partial class WpfApplicationTestHost
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
                AddResources(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });

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

    /// <summary>
    /// Sets the application resources as the Foundry.WinPE host does: the application dictionary is attached first, then the
    /// merged dictionaries are added in order, Fluent first, so each StaticResource finds the styles merged before it.
    /// </summary>
    private static partial void AddResources(Application application);
}

/// <summary>Serializes the tests that share <see cref="WpfApplicationTestHost"/> and the process-wide binding trace source.</summary>
[CollectionDefinition(nameof(WpfApplicationHostCollection), DisableParallelization = true)]
public sealed class WpfApplicationHostCollection;
