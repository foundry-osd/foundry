// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;
using Foundry.Avalonia.Services.Threading;

namespace Foundry.Connect.Platform;

internal sealed class WpfUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new WpfUiTimer(interval);

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfUiTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = interval
            };
            _timer.Tick += OnTick;
        }

        public bool IsEnabled => _timer.IsEnabled;

        public event EventHandler? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }

        private void OnTick(object? sender, EventArgs e) => Tick?.Invoke(this, e);
    }
}
