// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Runs test code on one process-wide STA thread that owns the test <see cref="Application"/>, with the resources Deploy merges:
/// the Fluent theme, the shared Foundry.Wpf styles and Deploy's styles.
/// </summary>
/// <remarks>
/// WPF allows one <see cref="Application"/> per process, and view models capture <c>Application.Current.Dispatcher</c>, so the
/// thread keeps pumping for the lifetime of the test process instead of being shut down after a test.
/// </remarks>
internal static class WpfApplicationTestHost
{
    private static readonly string[] DeployDictionaries =
    [
        "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
        "pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml",
        "pack://application:,,,/Foundry.Deploy;component/Themes/DeployStyles.xaml"
    ];

    private static readonly Lazy<Dispatcher> HostDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the exceptions that reached the host dispatcher without being observed by a test.</summary>
    public static ConcurrentQueue<Exception> UnhandledExceptions { get; } = new();

    /// <summary>Runs <paramref name="test"/> on the host thread, where awaits resume on the host dispatcher.</summary>
    public static Task RunAsync(Func<Task> test) => HostDispatcher.Value.InvokeAsync(test).Task.Unwrap();

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
                Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Same dictionaries, in the same order, as Deploy's App.xaml, which the Foundry.WinPE host loads in Deploy mode.
                foreach (string source in DeployDictionaries)
                {
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
                }

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
