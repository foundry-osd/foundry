using Microsoft.UI.Dispatching;

namespace Foundry.Services.Operations;

public sealed class OperationProgressService : IOperationProgressService
{
    private static readonly TimeSpan TerminalStatusRetention = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private bool _isOperationInProgress;
    private int _progress;
    private string? _status;
    private OperationKind? _currentOperation;
    private long _stateVersion;
    private CancellationTokenSource? _pendingResetCts;

    public bool IsOperationInProgress
    {
        get
        {
            lock (_sync)
            {
                return _isOperationInProgress;
            }
        }
    }

    public int Progress
    {
        get
        {
            lock (_sync)
            {
                return _progress;
            }
        }
    }

    public string? Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public OperationKind? CurrentOperation
    {
        get
        {
            lock (_sync)
            {
                return _currentOperation;
            }
        }
    }

    public bool CanStartOperation
    {
        get
        {
            lock (_sync)
            {
                return !_isOperationInProgress;
            }
        }
    }

    public event EventHandler? ProgressChanged;

    public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0)
    {
        lock (_sync)
        {
            if (_isOperationInProgress)
            {
                return false;
            }

            CancelPendingReset_NoLock();
            _isOperationInProgress = true;
            _currentOperation = kind;
            _progress = Math.Clamp(initialProgress, 0, 100);
            _status = initialStatus;
            _stateVersion++;
        }

        RaiseProgressChanged();
        return true;
    }

    public void Report(int progress, string? status = null)
    {
        lock (_sync)
        {
            if (!_isOperationInProgress)
            {
                return;
            }

            var normalizedProgress = Math.Clamp(progress, 0, 100);
            _progress = Math.Max(_progress, normalizedProgress);

            if (status is not null)
            {
                _status = status;
            }
        }

        RaiseProgressChanged();
    }

    public void Complete(string? status = null)
    {
        long completedVersion;
        lock (_sync)
        {
            if (!_isOperationInProgress)
            {
                return;
            }

            _progress = 100;
            if (status is not null)
            {
                _status = status;
            }

            _isOperationInProgress = false;
            _currentOperation = null;
            _stateVersion++;
            completedVersion = _stateVersion;
        }

        RaiseProgressChanged();
        ScheduleDelayedReset(completedVersion);
    }

    public void Fail(string status)
    {
        long failedVersion;
        lock (_sync)
        {
            if (!_isOperationInProgress)
            {
                return;
            }

            _status = status;
            _isOperationInProgress = false;
            _currentOperation = null;
            _stateVersion++;
            failedVersion = _stateVersion;
        }

        RaiseProgressChanged();
        ScheduleDelayedReset(failedVersion);
    }

    public void ResetToIdle()
    {
        lock (_sync)
        {
            CancelPendingReset_NoLock();
            _isOperationInProgress = false;
            _currentOperation = null;
            _progress = 0;
            _status = null;
            _stateVersion++;
        }

        RaiseProgressChanged();
    }

    private void ScheduleDelayedReset(long expectedStateVersion)
    {
        var cts = new CancellationTokenSource();

        lock (_sync)
        {
            _pendingResetCts?.Cancel();
            _pendingResetCts?.Dispose();
            _pendingResetCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TerminalStatusRetention, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            bool shouldReset;
            lock (_sync)
            {
                shouldReset = !cts.IsCancellationRequested &&
                              !_isOperationInProgress &&
                              _status is not null &&
                              _stateVersion == expectedStateVersion;
            }

            if (shouldReset)
            {
                ResetToIdle();
            }
        });
    }

    private void CancelPendingReset_NoLock()
    {
        _pendingResetCts?.Cancel();
        _pendingResetCts?.Dispose();
        _pendingResetCts = null;
    }

    private void RaiseProgressChanged()
    {
        EventHandler? handler = ProgressChanged;
        if (handler is null)
        {
            return;
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => handler(this, EventArgs.Empty));
    }
}
