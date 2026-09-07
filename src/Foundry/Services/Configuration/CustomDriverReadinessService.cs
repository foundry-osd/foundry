// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Publishes only the latest driver-path inspection while retaining blocked native enumeration ownership.</summary>
public sealed partial class CustomDriverReadinessService : ICustomDriverReadinessService
{
    private readonly object sync = new();
    private readonly CustomDriverSourceInspector inspector;
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IAppDispatcher dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? scanCancellation;
    private Task worker = Task.CompletedTask;
    private CustomDriverSourceInspection current = new("", CustomDriverSourceState.Empty, null);
    private string requestedPath = "";
    private long revision;
    private long publishedRevision;
    private bool disposed;

    public CustomDriverReadinessService(CustomDriverSourceInspector inspector,
        IFoundryConfigurationStateService configurationStateService, IAppDispatcher dispatcher)
    {
        this.inspector = inspector;
        configuration = configurationStateService;
        this.dispatcher = dispatcher;
        configuration.StateChanged += OnStateChanged;
        RequestInspection(configuration.Current.General.CustomDriverDirectoryPath);
    }

    public CustomDriverSourceInspection Current { get { lock (sync) return current; } }
    public event EventHandler? Changed;

    public void RequestInspection(string? path)
    {
        string normalized = path?.Trim() ?? "";
        long requestedRevision;
        lock (sync)
        {
            if (disposed || (revision != 0 && string.Equals(requestedPath, normalized, StringComparison.Ordinal))) return;
            requestedPath = normalized;
            requestedRevision = ++revision;
            scanCancellation?.Cancel();
            // Reserve the worker under the lock; execution begins off the dispatcher.
            if (worker.IsCompleted) worker = Task.Run(RunWorkerAsync);
        }
        PublishInspection(requestedRevision, new(normalized,
            normalized.Length == 0 ? CustomDriverSourceState.Empty : CustomDriverSourceState.Pending, null));
    }

    private void OnStateChanged(object? sender, EventArgs e) => RequestInspection(configuration.Current.General.CustomDriverDirectoryPath);

    private async Task RunWorkerAsync()
    {
        while (true)
        {
            long activeRevision;
            string path;
            CancellationTokenSource cancellation;
            lock (sync)
            {
                if (disposed) return;
                activeRevision = revision;
                path = requestedPath;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                scanCancellation = cancellation;
            }
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation.Token).ConfigureAwait(false);
                Task<CustomDriverSourceInspection> scan = inspector.InspectAsync(path, cancellation.Token);
                Task deadline = Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token);
                if (await Task.WhenAny(scan, deadline).ConfigureAwait(false) != scan)
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        PublishInspection(activeRevision, new(path, CustomDriverSourceState.TimedOut, "driver_inspection_timed_out"));
                        cancellation.Cancel();
                    }
                }
                // Cancellation/deadline does not abandon an enumeration blocked in native IO.
                CustomDriverSourceInspection result = await scan.ConfigureAwait(false);
                if (!cancellation.IsCancellationRequested) PublishInspection(activeRevision, result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception)
            {
                PublishInspection(activeRevision, new(path, CustomDriverSourceState.Inaccessible, "driver_inspection_failed"));
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null;
                }
                cancellation.Dispose();
            }
            lock (sync)
            {
                if (disposed || revision == activeRevision)
                {
                    worker = Task.CompletedTask;
                    return;
                }
            }
        }
    }

    private void PublishInspection(long resultRevision, CustomDriverSourceInspection result)
    {
        dispatcher.TryEnqueue(() =>
        {
            lock (sync)
            {
                if (disposed || resultRevision != revision || !string.Equals(result.Path, requestedPath, StringComparison.Ordinal)) return;
                if (publishedRevision == resultRevision && result.State == CustomDriverSourceState.Pending &&
                    current.State != CustomDriverSourceState.Pending) return;
                publishedRevision = resultRevision;
                current = result;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task StopAsync(CancellationToken waitToken = default)
    {
        Dispose();
        Task pending;
        lock (sync) pending = worker;
        await pending.WaitAsync(waitToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            scanCancellation?.Cancel();
        }
        configuration.StateChanged -= OnStateChanged;
        _ = DisposeLifetimeAfterWorkerAsync();
    }

    private async Task DisposeLifetimeAfterWorkerAsync()
    {
        Task pending;
        lock (sync) pending = worker;
        try { await pending.ConfigureAwait(false); }
        catch (Exception) { }
        finally { lifetime.Dispose(); }
    }
}
