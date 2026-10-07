// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Deployment.Native.Servicing;

/// <summary>Owns one complete DISM operation in a dedicated worker process.</summary>
internal sealed class NativeDismOperations(IDismNativeApi api)
{
    private const int UnexpectedFailure = unchecked((int)0x80004005);
    private const uint MaximumInventoryCount = 1_000_000;

    /// <summary>Uses the worker's System32-only DISM adapter.</summary>
    internal NativeDismOperations() : this(new NativeDismApi()) { }

    /// <summary>Executes the requested operation and releases its native resources before returning.</summary>
    internal NativeWorkerResult Execute(NativeWorkerRequest request, Action<NativeWorkerMessage> emit)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(emit);
        if (request.Operation == NativeWorkerOperation.ProbeDism)
        {
            api.EnsureAvailable();
            return new NativeWorkerResult();
        }

        if (request.Operation is not (NativeWorkerOperation.ReadFeatures or NativeWorkerOperation.DisableFeature or
            NativeWorkerOperation.AddDrivers or NativeWorkerOperation.MountImage or NativeWorkerOperation.UnmountImage or NativeWorkerOperation.InspectMount))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "This worker operation is not a DISM operation.");
        }

        Check("DismInitialize", api.Initialize(2, request.LogFilePath, request.ScratchDirectory));
        return WithCleanup(request.Operation.ToString(), () => ExecuteInitialized(request, emit),
            () => Check("DismShutdown", api.Shutdown(), captureDetail: false));
    }

    private NativeWorkerResult ExecuteInitialized(NativeWorkerRequest request, Action<NativeWorkerMessage> emit) => request.Operation switch
    {
        NativeWorkerOperation.ReadFeatures => WithSession(request.WindowsRoot, ReadFeatures),
        NativeWorkerOperation.DisableFeature => WithSession(request.WindowsRoot, session =>
        {
            WithProgress("DismDisableFeature", emit, callback => Check("DismDisableFeature",
                api.DisableFeature(session, request.FeatureName, false, callback), mutation: true));
            return new NativeWorkerResult();
        }),
        NativeWorkerOperation.AddDrivers => AddDrivers(request, emit),
        NativeWorkerOperation.MountImage => MountImage(request, emit),
        NativeWorkerOperation.UnmountImage => UnmountImage(request, emit),
        NativeWorkerOperation.InspectMount => InspectMount(request.MountPath),
        _ => throw new ArgumentOutOfRangeException(nameof(request))
    };

    private NativeWorkerResult WithSession(string windowsRoot, Func<uint, NativeWorkerResult> operation)
    {
        Check("DismOpenSession", api.OpenSession(windowsRoot, out uint session));
        return WithCleanup("DismSession", () => operation(session), () => Check("DismCloseSession", api.CloseSession(session)));
    }

    private NativeWorkerResult ReadFeatures(uint session)
    {
        IntPtr features = IntPtr.Zero;
        return WithCleanup("DismGetFeatures", () =>
        {
            Check("DismGetFeatures", api.GetFeatures(session, out features, out uint count));
            ValidateBuffer(features, count);
            int size = Marshal.SizeOf<DismFeature>();
            var result = new NativeWindowsFeature[checked((int)count)];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < result.Length; index++)
            {
                var feature = Marshal.PtrToStructure<DismFeature>(IntPtr.Add(features, checked(index * size)));
                string name = Marshal.PtrToStringUni(feature.Name) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                {
                    throw new InvalidDataException("DISM returned an empty or duplicate feature name.");
                }

                OfflineWindowsFeatureState state = feature.State switch
                {
                    0 or 2 => OfflineWindowsFeatureState.Disabled,
                    1 => OfflineWindowsFeatureState.DisablePending,
                    3 => OfflineWindowsFeatureState.PayloadRemoved,
                    4 => OfflineWindowsFeatureState.Enabled,
                    5 => OfflineWindowsFeatureState.EnablePending,
                    _ => throw new InvalidDataException($"DISM returned unsupported feature state {feature.State} for '{name}'.")
                };
                result[index] = new NativeWindowsFeature(name, state);
            }

            return new NativeWorkerResult { Features = result };
        }, () => Delete(features));
    }

    private NativeWorkerResult AddDrivers(NativeWorkerRequest request, Action<NativeWorkerMessage> emit)
    {
        if ((File.GetAttributes(request.DriverRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The staged driver root must be a physical directory.");
        }

        // Exclude junctions and symbolic links so discovery cannot leave the staged payload or recurse indefinitely.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        string[] drivers = Directory.EnumerateFiles(request.DriverRoot, "*.inf", options)
            .Order(StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal).ToArray();
        if (drivers.Length == 0) throw new FileNotFoundException("The staged driver payload contains no INF files.", request.DriverRoot);
        return WithSession(request.WindowsRoot, session =>
        {
            var failures = new List<NativeDriverFailure>();
            for (int index = 0; index < drivers.Length; index++)
            {
                // dism.exe /Add-Driver on a folder skips INF files that are not valid driver packages. One rejected INF
                // must not withhold the remaining drivers, so each rejection is reported instead of ending the operation.
                try
                {
                    Check("DismAddDriver", api.AddDriver(session, drivers[index], false), mutation: true);
                }
                catch (NativeOperationException exception)
                {
                    failures.Add(new NativeDriverFailure(drivers[index], exception.ErrorCode, exception.Message));
                }

                emit(new NativeWorkerMessage { Percent = (index + 1d) / drivers.Length * 100d, Phase = "DismAddDriver" });
            }

            if (failures.Count == drivers.Length)
            {
                throw new NativeOperationException("DismAddDriver", failures[0].ErrorCode,
                    $"None of the {drivers.Length} staged INF files could be added. First failure ({failures[0].InfPath}): {failures[0].Message}");
            }

            return new NativeWorkerResult { DriversAdded = drivers.Length - failures.Count, DriverFailures = [.. failures] };
        });
    }

    private NativeWorkerResult MountImage(NativeWorkerRequest request, Action<NativeWorkerMessage> emit)
    {
        if (request.ImageIndex <= 0) throw new ArgumentOutOfRangeException(nameof(request), "An image index must be positive.");
        WithProgress("DismMountImage", emit, callback => Check("DismMountImage",
            api.MountImage(request.ImagePath, (uint)request.ImageIndex, request.MountPath, 0, callback), mutation: true));
        return new NativeWorkerResult();
    }

    private NativeWorkerResult UnmountImage(NativeWorkerRequest request, Action<NativeWorkerMessage> emit)
    {
        WithProgress("DismUnmountImage", emit, callback => Check("DismUnmountImage",
            api.UnmountImage(request.MountPath, request.Commit ? 0u : 1u, callback), mutation: true));
        return new NativeWorkerResult();
    }

    private NativeWorkerResult InspectMount(string mountPath)
    {
        IntPtr images = IntPtr.Zero;
        return WithCleanup("DismGetMountedImageInfo", () =>
        {
            Check("DismGetMountedImageInfo", api.GetMountedImages(out images, out uint count));
            ValidateBuffer(images, count);
            string requestedPath = NormalizeMountPath(mountPath);
            int size = Marshal.SizeOf<DismMountedImage>();
            bool registered = false;
            for (int index = 0; index < (int)count; index++)
            {
                var image = Marshal.PtrToStructure<DismMountedImage>(IntPtr.Add(images, checked(index * size)));
                string path = Marshal.PtrToStringUni(image.MountPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("DISM returned an empty mounted-image path.");
                // Invalid and remount-needed registrations still prohibit deleting the owned directory.
                registered |= StringComparer.OrdinalIgnoreCase.Equals(requestedPath, NormalizeMountPath(path));
            }

            return new NativeWorkerResult { MountRegistered = registered };
        }, () => Delete(images));
    }

    private static string NormalizeMountPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ValidateBuffer(IntPtr pointer, uint count)
    {
        if (count > MaximumInventoryCount || (count > 0 && pointer == IntPtr.Zero))
        {
            throw new InvalidDataException("DISM returned an invalid inventory allocation or count.");
        }
    }

    private static void WithProgress(string function, Action<NativeWorkerMessage> emit, Action<DismProgressCallback> operation)
    {
        var callbackGate = new object();
        Exception? callbackFailure = null;
        DismProgressCallback callback = (current, total, _) =>
        {
            lock (callbackGate)
            {
                if (total == 0 || Volatile.Read(ref callbackFailure) is not null) return;
                try
                {
                    emit(new NativeWorkerMessage { Percent = Math.Clamp(current / (double)total * 100d, 0d, 100d), Phase = function });
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref callbackFailure, exception, null);
                }
            }
        };
        try
        {
            operation(callback);
        }
        catch (NativeOperationException exception)
        {
            Exception? reportedFailure = Volatile.Read(ref callbackFailure);
            if (reportedFailure is not null) exception.CleanupErrors.Add($"Progress reporting failed: {reportedFailure.Message}");
            throw;
        }
        finally
        {
            GC.KeepAlive(callback);
        }

        Exception? progressFailure = Volatile.Read(ref callbackFailure);
        if (progressFailure is not null)
        {
            throw new NativeOperationException(function, UnexpectedFailure, "DISM progress reporting failed after native execution completed.", progressFailure);
        }
    }

    /// <summary>
    /// Throws for any result outside the accepted set. Mutations also accept the documented success values
    /// DISMAPI_S_RELOAD_IMAGE_SESSION_REQUIRED (1) and ERROR_SUCCESS_REBOOT_REQUIRED (3010); other positive values stay failures.
    /// </summary>
    private void Check(string function, int result, bool mutation = false, bool captureDetail = true)
    {
        if (result == 0 || (mutation && result is 1 or 3010)) return;
        var failure = new NativeOperationException(function, result, $"{function} failed with HRESULT 0x{result:X8}.");
        if (!captureDetail) throw failure;

        IntPtr message = IntPtr.Zero;
        string? detail = null;
        try
        {
            if (api.GetLastErrorMessage(out message) == 0 && message != IntPtr.Zero)
            {
                detail = Marshal.PtrToStringUni(Marshal.ReadIntPtr(message));
            }
        }
        catch (Exception exception)
        {
            failure.CleanupErrors.Add($"Native error detail could not be read: {exception.Message}");
        }
        finally
        {
            if (message != IntPtr.Zero)
            {
                try
                {
                    int deleteResult = api.Delete(message);
                    if (deleteResult != 0) failure.CleanupErrors.Add($"DismDelete(error message) failed with HRESULT 0x{deleteResult:X8}.");
                }
                catch (Exception exception)
                {
                    failure.CleanupErrors.Add($"DismDelete(error message) failed: {exception.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            var detailed = new NativeOperationException(function, result, $"{failure.Message} {detail.Trim()}");
            detailed.CleanupErrors.AddRange(failure.CleanupErrors);
            throw detailed;
        }

        throw failure;
    }

    private void Delete(IntPtr allocation)
    {
        if (allocation != IntPtr.Zero) Check("DismDelete", api.Delete(allocation), captureDetail: false);
    }

    private static NativeWorkerResult WithCleanup(string function, Func<NativeWorkerResult> operation, Action cleanup)
    {
        NativeWorkerResult? result = null;
        NativeOperationException? failure = null;
        try { result = operation(); }
        catch (Exception exception) { failure = NormalizeFailure(function, exception); }
        try { cleanup(); }
        catch (Exception exception)
        {
            NativeOperationException cleanupFailure = NormalizeFailure(function, exception);
            if (failure is null) failure = cleanupFailure;
            else
            {
                failure.CleanupErrors.Add(cleanupFailure.Message);
                failure.CleanupErrors.AddRange(cleanupFailure.CleanupErrors);
            }
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    private static NativeOperationException NormalizeFailure(string function, Exception exception) => exception as NativeOperationException
        ?? new NativeOperationException(function, exception.HResult, exception.Message, exception);

    // DISM's SDK declares these structures inside #pragma pack(push, 1), on every architecture.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DismFeature
    {
        internal IntPtr Name;
        internal int State;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DismMountedImage
    {
        internal IntPtr MountPath;
        internal IntPtr ImagePath;
        internal uint ImageIndex;
        internal int MountMode;
        internal int MountStatus;
    }
}
