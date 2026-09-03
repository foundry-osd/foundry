// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    public static WinPeResult Failure(
        string code,
        string message,
        string? details = null,
        string? stage = null,
        string? command = null,
        int? exitCode = null,
        string? failureKind = null,
        string? failureReason = null,
        string? toolName = null,
        string? errorSummary = null,
        int retryCount = 0,
        Exception? exception = null)
    {
        return new WinPeResult(false, new WinPeDiagnostic(
            code, message, details, stage, command, exitCode, failureKind, failureReason,
            toolName, errorSummary, retryCount, exception));
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

    public new static WinPeResult<T> Failure(
        string code,
        string message,
        string? details = null,
        string? stage = null,
        string? command = null,
        int? exitCode = null,
        string? failureKind = null,
        string? failureReason = null,
        string? toolName = null,
        string? errorSummary = null,
        int retryCount = 0,
        Exception? exception = null)
    {
        return new WinPeResult<T>(false, default, new WinPeDiagnostic(
            code, message, details, stage, command, exitCode, failureKind, failureReason,
            toolName, errorSummary, retryCount, exception));
    }
}
