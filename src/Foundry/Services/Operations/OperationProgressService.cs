namespace Foundry.Services.Operations;

public sealed class OperationProgressService : IOperationProgressService
{
    private readonly object _sync = new();
    private bool _isOperationInProgress;
    private int _progress;
    private string? _status;
    private OperationKind? _currentOperation;

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

            _isOperationInProgress = true;
            _currentOperation = kind;
            _progress = Math.Clamp(initialProgress, 0, 100);
            _status = initialStatus;
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
        }

        RaiseProgressChanged();
    }

    public void Fail(string status)
    {
        lock (_sync)
        {
            if (!_isOperationInProgress)
            {
                return;
            }

            _status = status;
        }

        RaiseProgressChanged();
    }

    public void ResetToIdle()
    {
        lock (_sync)
        {
            _isOperationInProgress = false;
            _currentOperation = null;
            _progress = 0;
            _status = null;
        }

        RaiseProgressChanged();
    }

    private void RaiseProgressChanged()
    {
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
}
