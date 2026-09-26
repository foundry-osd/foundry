// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Reads Unicode mounted-image paths without relying on DISM console encoding.
/// </summary>
/// <remarks>
/// The production adapter shares DISM initialization with other native image consumers. Reads,
/// buffer release, and disposal share a lock so its lifetime lease outlives every native call.
/// </remarks>
internal sealed class NativeWinPeMountedImageInventory : IDisposable
{
    private static readonly Lazy<NativeWinPeMountedImageInventory> Production = new(CreateProduction);
    private readonly IWinPeMountedImageApi _api;
    private readonly object _gate = new();
    private bool _initialized;
    private bool _disposed;

    /// <summary>Creates an isolated reader whose native ownership can be tested without loading DISM.</summary>
    internal NativeWinPeMountedImageInventory(IWinPeMountedImageApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>Returns a fresh snapshot from the lazy process-owned native reader.</summary>
    internal static IReadOnlyList<WinPeMountedImage> GetMountedImages() => Production.Value.Read();

    /// <summary>Copies every registered mount before releasing the native allocation.</summary>
    internal IReadOnlyList<WinPeMountedImage> Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized)
            {
                ThrowIfFailed(_api.Initialize(), "DismInitialize");
                _initialized = true;
            }

            IntPtr buffer = IntPtr.Zero;
            Exception? readFailure = null;
            try
            {
                ThrowIfFailed(_api.GetMountedImageInfo(out buffer, out uint count), "DismGetMountedImageInfo");
                return CopyImages(buffer, count);
            }
            catch (Exception exception)
            {
                readFailure = exception;
                throw;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    try
                    {
                        ThrowIfFailed(_api.Delete(buffer), "DismDelete");
                    }
                    catch (Exception) when (readFailure is not null)
                    {
                        // The failed read remains the reason cleanup must retain the workspace.
                    }
                }
            }
        }
    }

    /// <summary>Waits for active reads and releases only the initialization owned by this reader.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_initialized)
            {
                ThrowIfFailed(_api.Shutdown(), "DismShutdown");
            }
        }
    }

    private static NativeWinPeMountedImageInventory CreateProduction()
    {
        var inventory = new NativeWinPeMountedImageInventory(new NativeWinPeMountedImageApi());
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                inventory.Dispose();
            }
            catch
            {
                // Process shutdown must not replace the application's original outcome.
            }
        };
        return inventory;
    }

    private static WinPeMountedImage[] CopyImages(IntPtr buffer, uint count)
    {
        if (count > 0 && buffer == IntPtr.Zero)
        {
            throw new InvalidDataException("DISM returned a mounted-image count without an inventory buffer.");
        }

        int imageCount = checked((int)count);
        int stride = Marshal.SizeOf<DismMountedImageInfo>();
        _ = checked(imageCount * stride);
        var images = new WinPeMountedImage[imageCount];
        for (int index = 0; index < imageCount; index++)
        {
            DismMountedImageInfo image = Marshal.PtrToStructure<DismMountedImageInfo>(IntPtr.Add(buffer, checked(index * stride)));
            string? mountPath = Marshal.PtrToStringUni(image.MountPath);
            string? imagePath = Marshal.PtrToStringUni(image.ImageFilePath);
            if (string.IsNullOrWhiteSpace(mountPath) || string.IsNullOrWhiteSpace(imagePath))
            {
                throw new InvalidDataException("DISM returned a mounted image without complete mount and image paths.");
            }

            // Invalid and remount-needed registrations also prohibit workspace deletion.
            images[index] = new WinPeMountedImage(mountPath, imagePath);
        }

        return images;
    }

    private static void ThrowIfFailed(int hResult, string operation)
    {
        if (hResult != 0)
        {
            throw new COMException($"{operation} failed with HRESULT 0x{hResult:X8}.", hResult);
        }
    }

    // DismAPI.h packs this complete structure to one byte; x64 and ARM64 array stride is 28 bytes.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DismMountedImageInfo
    {
        public IntPtr MountPath;
        public IntPtr ImageFilePath;
        public uint ImageIndex;
        public uint MountMode;
        public uint MountStatus;
    }
}
