// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class RemoteDiagnosticsLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_RevokesBeforeDisposalEvenWhenFlushFails(bool fail)
    {
        var service = new LifecycleService(fail);
        Exception? error = await Record.ExceptionAsync(() => RemoteDiagnosticsLifecycle.ShutdownAsync(service, TestContext.Current.CancellationToken));
        Assert.Equal(fail, error is InvalidOperationException);
        Assert.True(service.DisabledAtDisposal);
    }

    private sealed class LifecycleService(bool fail) : IRemoteDiagnosticsService
    {
        private bool disabled;
        public bool DisabledAtDisposal { get; private set; }
        public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context) { }
        public void Emit(LogEvent logEvent) { }
        public void Disable() => disabled = true;
        public Task FlushAsync(CancellationToken cancellationToken) => fail ? Task.FromException(new InvalidOperationException("synthetic flush failure")) : Task.CompletedTask;
        public ValueTask DisposeAsync() { DisabledAtDisposal = disabled; return ValueTask.CompletedTask; }
    }
}
