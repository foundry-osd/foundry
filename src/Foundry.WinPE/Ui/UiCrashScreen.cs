// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;

namespace Foundry.WinPE.Ui;

/// <summary>Shows the crash window for the guardian and returns the operator's choice.</summary>
internal interface IUiCrashScreen
{
    /// <summary>Shows the crash window, with "Relaunch" only when <paramref name="relaunchAllowed"/>, until the operator answers.</summary>
    /// <param name="relaunchAllowed">Whether no destructive step had started, as recorded in the UI state file.</param>
    /// <param name="culture">The culture recorded in the UI state file, or <see langword="null"/> when none is known.</param>
    /// <param name="cancellationToken">Stops waiting for the answer; the window itself stays until the process ends.</param>
    Task<UiCrashChoice> ShowAsync(bool relaunchAllowed, string? culture, CancellationToken cancellationToken);
}

/// <summary>
/// Shows the <see cref="UiCrashWindow"/> screen from the guardian, whose own code runs on thread-pool threads. A process has one WPF
/// application, bound to the thread that created it, so every crash window of the guardian runs on one background STA
/// thread started on first use; it ends with the process.
/// </summary>
internal sealed class UiCrashScreen(string? logDirectory) : IUiCrashScreen
{
    private readonly Lazy<Dispatcher> _dispatcher = new(StartWindowThread);

    /// <inheritdoc />
    public Task<UiCrashChoice> ShowAsync(bool relaunchAllowed, string? culture, CancellationToken cancellationToken)
    {
        var choice = new TaskCompletionSource<UiCrashChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Value.BeginInvoke(() =>
        {
            try
            {
                choice.SetResult(UiCrashWindow.Show(relaunchAllowed, logDirectory, culture));
            }
            catch (Exception exception)
            {
                choice.SetException(exception);
            }
        });
        return choice.Task.WaitAsync(cancellationToken);
    }

    private static Dispatcher StartWindowThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Foundry crash window"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
