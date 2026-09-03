// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

using Foundry.Utilities.Diagnostics;

public sealed record WinPeDiagnostic
{
    public WinPeDiagnostic(
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
        Code = code;
        Message = message;
        Details = details;
        Stage = stage;
        Command = command;
        ExitCode = exitCode;
        ToolName = toolName;
        ErrorSummary = string.IsNullOrWhiteSpace(errorSummary)
            ? null
            : DiagnosticContentSanitizer.Sanitize(errorSummary, 512);
        RetryCount = Math.Max(0, retryCount);
        Exception = exception;
        (FailureKind, FailureReason) = Classify(code, exitCode, toolName, exception, failureKind, failureReason);
    }

    public string Code { get; init; }
    public string Message { get; init; }
    public string? Details { get; init; }
    public string? Stage { get; init; }
    public string? Command { get; init; }
    public int? ExitCode { get; init; }
    public string FailureKind { get; init; }
    public string FailureReason { get; init; }
    public string? ToolName { get; init; }
    public string? ErrorSummary { get; init; }
    public int RetryCount { get; init; }
    public Exception? Exception { get; init; }

    private static (string Kind, string Reason) Classify(
        string code,
        int? exitCode,
        string? toolName,
        Exception? exception,
        string? failureKind,
        string? failureReason)
    {
        if (!string.IsNullOrWhiteSpace(failureKind) && !string.IsNullOrWhiteSpace(failureReason))
        {
            return (failureKind, failureReason);
        }

        (string Kind, string Reason) classification = exception switch
        {
            OperationCanceledException => (WinPeFailureKinds.Cancellation, WinPeFailureReasons.Cancelled),
            TimeoutException => (WinPeFailureKinds.Network, WinPeFailureReasons.Timeout),
            UnauthorizedAccessException => (WinPeFailureKinds.FileSystem, WinPeFailureReasons.AccessDenied),
            HttpRequestException { StatusCode: not null } => (WinPeFailureKinds.Network, WinPeFailureReasons.HttpStatus),
            HttpRequestException => (WinPeFailureKinds.Network, WinPeFailureReasons.Transport),
            _ when exitCode.HasValue => (WinPeFailureKinds.Process, WinPeFailureReasons.NonZeroExit),
            _ when exception is not null && !string.IsNullOrWhiteSpace(toolName) =>
                (WinPeFailureKinds.Process, WinPeFailureReasons.ProcessStartFailed),
            _ => ClassifyCode(code)
        };

        return (
            string.IsNullOrWhiteSpace(failureKind) ? classification.Kind : failureKind,
            string.IsNullOrWhiteSpace(failureReason) ? classification.Reason : failureReason);
    }

    private static (string Kind, string Reason) ClassifyCode(string code)
    {
        return code switch
        {
            WinPeErrorCodes.ValidationFailed => (WinPeFailureKinds.Validation, WinPeFailureReasons.InvalidInput),
            WinPeErrorCodes.OperationCancelled => (WinPeFailureKinds.Cancellation, WinPeFailureReasons.Cancelled),
            WinPeErrorCodes.ToolNotFound => (WinPeFailureKinds.Tooling, WinPeFailureReasons.ToolNotFound),
            WinPeErrorCodes.UsbUnsafeTarget or WinPeErrorCodes.UsbIdentityMismatch or WinPeErrorCodes.UsbVerificationFailed =>
                (WinPeFailureKinds.Validation, WinPeFailureReasons.DiskValidation),
            WinPeErrorCodes.DownloadFailed or WinPeErrorCodes.DriverCatalogFetchFailed or WinPeErrorCodes.OperatingSystemCatalogFetchFailed =>
                (WinPeFailureKinds.Network, WinPeFailureReasons.Transport),
            WinPeErrorCodes.IsoCreateFailed => (WinPeFailureKinds.Process, WinPeFailureReasons.IsoCreation),
            WinPeErrorCodes.InternalError => (WinPeFailureKinds.Internal, WinPeFailureReasons.Unexpected),
            _ => (WinPeFailureKinds.Internal, WinPeFailureReasons.Unexpected)
        };
    }
}
