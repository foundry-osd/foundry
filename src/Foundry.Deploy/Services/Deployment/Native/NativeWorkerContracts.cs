// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Native;

/// <summary>Operations executed in a fresh process so DISM initializes once with operation-owned scratch.</summary>
internal enum NativeWorkerOperation
{
    ProbeDism,
    ProbeWim,
    ReadFeatures,
    DisableFeature,
    AddDrivers,
    MountImage,
    UnmountImage,
    InspectMount,
    ApplyWim
}

/// <summary>Local worker input. Contains paths and servicing options, never deployment credentials.</summary>
internal sealed record NativeWorkerRequest
{
    public NativeWorkerOperation Operation { get; init; }
    public string WindowsRoot { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public string DriverRoot { get; init; } = string.Empty;
    public string MountPath { get; init; } = string.Empty;
    public string FeatureName { get; init; } = string.Empty;
    public string ScratchDirectory { get; init; } = string.Empty;
    public int ImageIndex { get; init; } = 1;
    public string? LogFilePath { get; init; }
    public bool Commit { get; init; }
    /// <summary>
    /// Names the parent's manual-reset event. When signaled, image application, driver injection and mounting stop at
    /// their next safe point; inventories, feature changes and unmounts always run to completion.
    /// </summary>
    public string? CancelEventName { get; init; }
}

/// <summary>A feature returned from the typed DISM inventory.</summary>
internal sealed record NativeWindowsFeature(string Name, OfflineWindowsFeatureState State);

/// <summary>An INF that DISM rejected while the rest of the staged payload was still processed.</summary>
internal sealed record NativeDriverFailure(string InfPath, int ErrorCode, string Message);

/// <summary>Successful worker output; a missing mount registration is distinct from a failed inventory.</summary>
internal sealed record NativeWorkerResult
{
    public NativeWindowsFeature[] Features { get; init; } = [];
    public bool MountRegistered { get; init; }
    public int DriversAdded { get; init; }
    public NativeDriverFailure[] DriverFailures { get; init; } = [];
}

/// <summary>Preserves native status separately from the worker's process exit code.</summary>
internal sealed record NativeWorkerError(string Function, int ErrorCode, string Message, string[] CleanupErrors);

/// <summary>One newline-delimited worker event. Only complete events carry a terminal result or error.</summary>
internal sealed record NativeWorkerMessage
{
    public string Kind { get; init; } = "progress";
    public double? Percent { get; init; }
    public string? Phase { get; init; }
    public NativeWorkerResult? Result { get; init; }
    public NativeWorkerError? Error { get; init; }
}

/// <summary>Keeps the original native failure while retaining secondary release failures for diagnostics.</summary>
internal sealed class NativeOperationException(string function, int errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>ERROR_REQUEST_ABORTED, the Win32 status WIMGAPI reports after a callback aborts an image.</summary>
    internal const int RequestAborted = 1235;

    /// <summary>ERROR_REQUEST_ABORTED mapped to an HRESULT for DISM operations stopped by the parent.</summary>
    internal const int RequestAbortedResult = unchecked((int)0x800704D3);

    public string Function { get; } = function;
    public int ErrorCode { get; } = errorCode;
    public List<string> CleanupErrors { get; } = [];
}
