// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Threading;

namespace Foundry.Avalonia.Services.Threading;

public sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;
    private bool _isDisposed;

    public AvaloniaUiTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer(
            interval,
            DispatcherPriority.Normal,
            Dispatcher.UIThread,
            OnTick);
    }

    public bool IsEnabled => _timer.IsEnabled;

    public event EventHandler? Tick;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        Tick = null;
        _isDisposed = true;
    }

    private void OnTick(object? sender, EventArgs eventArgs) => Tick?.Invoke(this, eventArgs);
}
