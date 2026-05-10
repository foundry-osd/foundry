namespace Foundry.Core.Services.WinPe;

public class WinPeResult
{
    protected WinPeResult(bool isSuccess, WinPeDiagnostic? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public WinPeDiagnostic? Error { get; }

    public static WinPeResult Success()
    {
        return new WinPeResult(true, null);
    }

    public static WinPeResult Failure(WinPeDiagnostic error)
    {
        return new WinPeResult(false, error);
    }

    public static WinPeResult Failure(string code, string message, string? details = null)
    {
        return new WinPeResult(false, new WinPeDiagnostic(code, message, details));
    }
}

public sealed class WinPeResult<T> : WinPeResult
{
    private WinPeResult(bool isSuccess, T? value, WinPeDiagnostic? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static WinPeResult<T> Success(T value)
    {
        return new WinPeResult<T>(true, value, null);
    }

    public new static WinPeResult<T> Failure(WinPeDiagnostic error)
    {
        return new WinPeResult<T>(false, default, error);
    }

    public new static WinPeResult<T> Failure(string code, string message, string? details = null)
    {
        return new WinPeResult<T>(false, default, new WinPeDiagnostic(code, message, details));
    }
}
