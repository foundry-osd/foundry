// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Imaging;

/// <summary>Applies one integrity-checked ordinary WIM and releases native handles and callback registrations before returning.</summary>
internal sealed class NativeWimOperations(INativeWimApi api)
{
    // WIMGAPI.H: WIM_FLAG_VERIFY on open checks archive integrity like dism.exe /CheckIntegrity. Passing it to
    // WIMApplyImage would add the per-file verification of /Verify, which this deployment path never requested.
    private const uint Verify = 0x00000002;
    private static readonly ConcurrentBag<WimMessageCallback> UnreleasedCallbacks = [];

    public NativeWimOperations() : this(new NativeWimApi()) { }

    /// <summary>Probes capabilities or applies the selected WIM index; other containers stay on the caller's explicit route.</summary>
    public NativeWorkerResult Execute(NativeWorkerRequest request, Action<NativeWorkerMessage> report)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);
        if (request.Operation == NativeWorkerOperation.ProbeWim)
        {
            api.Probe();
            return new();
        }
        if (request.Operation != NativeWorkerOperation.ApplyWim)
            throw new NotSupportedException("This worker operation is not a WIM imaging operation.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);
        if (!Path.GetExtension(request.ImagePath).Equals(".wim", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Native image application accepts ordinary WIM files only.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WindowsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScratchDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ImageIndex, 1);
        return Apply(request, report);
    }

    private NativeWorkerResult Apply(NativeWorkerRequest request, Action<NativeWorkerMessage> report)
    {
        nint wim = 0;
        nint image = 0;
        bool callbackRegistered = false;
        bool logRegistered = false;
        Exception? failure = null;
        var callbackState = new CallbackState(report);
        WimMessageCallback callback = callbackState.HandleMessage;
        nint callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
        try
        {
            if (!string.IsNullOrWhiteSpace(request.LogFilePath))
            {
                Ensure(api.RegisterLogFile(request.LogFilePath, 0), "WIMRegisterLogFile");
                logRegistered = true;
            }
            wim = api.CreateFile(request.ImagePath, 0x80000000, 3, Verify, 0);
            Ensure(wim != 0, "WIMCreateFile");
            Ensure(api.SetTemporaryPath(wim, request.ScratchDirectory), "WIMSetTemporaryPath");
            uint registration = api.RegisterMessageCallback(wim, callbackPointer);
            Ensure(registration != uint.MaxValue, "WIMRegisterMessageCallback");
            callbackRegistered = true;
            image = api.LoadImage(wim, checked((uint)request.ImageIndex));
            Ensure(image != 0, "WIMLoadImage");
            if (!api.ApplyImage(image, request.WindowsRoot, 0))
            {
                int nativeError = api.GetLastError();
                throw CreateError("WIMApplyImage", nativeError, callbackState.NativeError, callbackState.NativeErrorPath);
            }
            callbackState.ThrowIfFailed();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            callbackState.Stop();
            if (image != 0) Cleanup(() => api.CloseHandle(image), "WIMCloseHandle", ref failure);
            if (callbackRegistered)
            {
                bool unregistered = Cleanup(() => api.UnregisterMessageCallback(wim, callbackPointer), "WIMUnregisterMessageCallback", ref failure);
                // A failed unregister must never leave native code with a collectible function pointer.
                // The isolated worker exits after this operation, releasing this final safety root.
                if (!unregistered) UnreleasedCallbacks.Add(callback);
            }
            if (wim != 0) Cleanup(() => api.CloseHandle(wim), "WIMCloseHandle", ref failure);
            if (logRegistered) Cleanup(() => api.UnregisterLogFile(request.LogFilePath!), "WIMUnregisterLogFile", ref failure);
            GC.KeepAlive(callback);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return new();
    }

    private void Ensure(bool succeeded, string function)
    {
        if (!succeeded) throw CreateError(function, api.GetLastError());
    }

    private bool Cleanup(Func<bool> release, string function, ref Exception? failure)
    {
        try
        {
            Ensure(release(), function);
            return true;
        }
        catch (Exception exception)
        {
            if (failure is null) failure = exception;
            else if (failure is NativeOperationException native) native.CleanupErrors.Add(exception.Message);
            return false;
        }
    }

    private static NativeOperationException CreateError(string function, int errorCode, int? callbackError = null, string? failedPath = null)
    {
        string detail = new Win32Exception(errorCode).Message;
        string callbackDetail = callbackError.HasValue ? $" WIM callback error: {callbackError.Value}." : string.Empty;
        string pathDetail = string.IsNullOrWhiteSpace(failedPath) ? string.Empty : $" Failed path: {failedPath}.";
        return new(function, errorCode, $"{function} failed with Win32 error {errorCode}: {detail}.{callbackDetail}{pathDetail}");
    }

    private sealed class CallbackState(Action<NativeWorkerMessage> report)
    {
        private readonly object gate = new();
        private bool active = true;
        private int lastPercent = -1;
        private Exception? failure;
        private int? nativeError;
        private string? nativeErrorPath;

        public int? NativeError { get { lock (gate) return nativeError; } }

        public string? NativeErrorPath { get { lock (gate) return nativeErrorPath; } }

        public uint HandleMessage(uint message, nuint wParam, nint lParam, nint userData)
        {
            try
            {
                lock (gate)
                {
                    if (!active) return 0;
                    if (message == 0x947f)
                    {
                        if (nativeError is null)
                        {
                            // WIM_MSG_ERROR: wParam is the failing file's path, valid only for the duration of this callback.
                            nativeError = unchecked((int)lParam);
                            nativeErrorPath = wParam == 0 ? null : Marshal.PtrToStringUni(unchecked((nint)wParam));
                        }
                    }
                    else if (message == 0x9478 && failure is null && wParam <= 100)
                    {
                        int percent = (int)wParam;
                        if (percent > lastPercent)
                        {
                            lastPercent = percent;
                            report(new() { Percent = percent, Phase = "Applying Windows image" });
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                lock (gate) failure ??= exception;
            }
            // Never skip a failed file, abort active application, or throw across the native boundary.
            return 0;
        }

        public void ThrowIfFailed()
        {
            lock (gate)
            {
                if (failure is not null)
                    throw new NativeOperationException("WIMMessageCallback", failure.HResult, "The WIM progress callback failed.", failure);
                if (nativeError is int code) throw CreateError("WIMMessageCallback", code, failedPath: nativeErrorPath);
            }
        }

        public void Stop() { lock (gate) active = false; }
    }
}
