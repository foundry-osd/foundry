// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Utilities.Imaging;

/// <summary>
/// Owns native DISM image metadata reads for the application lifetime.
/// </summary>
/// <remarks>
/// Production adapters share a process-wide DISM lifetime. Each reader serializes its reads,
/// buffer release, and disposal so its lifetime lease cannot be released during a native call.
/// </remarks>
public sealed class NativeDismImageInfoReader : IDisposable
{
    private readonly IDismImageInfoApi _api;
    private readonly object _gate = new();
    private readonly Lazy<bool> _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates the application-owned reader without loading or initializing DISM until its first read.
    /// </summary>
    public NativeDismImageInfoReader()
        : this(new NativeDismImageInfoApi())
    {
    }

    /// <summary>
    /// Creates a reader with an isolated native ownership boundary.
    /// </summary>
    internal NativeDismImageInfoReader(IDismImageInfoApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _initialized = new Lazy<bool>(() =>
        {
            ThrowIfFailed(_api.Initialize(), "DismInitialize");
            return true;
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WindowsImageMetadata>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        return Task.Run(() => Read(imagePath, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Waits for any active native read and buffer release, then releases this reader's DISM lifetime lease once.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_initialized.IsValueCreated)
            {
                ThrowIfFailed(_api.Shutdown(), "DismShutdown");
            }
        }
    }

    private IReadOnlyList<WindowsImageMetadata> Read(string imagePath, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            _ = _initialized.Value;
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr buffer = IntPtr.Zero;
            int deleteResult = 0;
            WindowsImageMetadata[] images;
            try
            {
                int result = _api.GetImageInfo(imagePath, out buffer, out uint count);
                ThrowIfFailed(result, "DismGetImageInfo");
                images = CopyImageInfo(buffer, count);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    deleteResult = _api.Delete(buffer);
                }
            }

            // A read/conversion failure keeps its original exception; a successful read must also release successfully.
            ThrowIfFailed(deleteResult, "DismDelete");
            // DismGetImageInfo has no cancellation parameter. Never complete cancellation while it still owns a buffer.
            cancellationToken.ThrowIfCancellationRequested();
            return images;
        }
    }

    private static WindowsImageMetadata[] CopyImageInfo(IntPtr buffer, uint count)
    {
        if (count > 0 && buffer == IntPtr.Zero)
        {
            throw new InvalidDataException("DISM returned an image count without an image information buffer.");
        }

        if (count > 1024) throw new InvalidDataException("The image index count exceeds the supported metadata limit.");
        int imageCount = checked((int)count);
        int stride = Marshal.SizeOf<DismImageInfo>();
        _ = checked(imageCount * stride);
        var images = new WindowsImageMetadata[imageCount];
        for (int position = 0; position < images.Length; position++)
        {
            DismImageInfo image = Marshal.PtrToStructure<DismImageInfo>(IntPtr.Add(buffer, position * stride));
            images[position] = new WindowsImageMetadata(
                checked((int)image.ImageIndex),
                Marshal.PtrToStringUni(image.ImageName) ?? string.Empty,
                Marshal.PtrToStringUni(image.EditionId) ?? string.Empty,
                image.ImageSize,
                image.Architecture switch
                {
                    0 => "x86",
                    9 => "x64",
                    12 => "arm64",
                    _ => "unknown"
                },
                new Version(checked((int)image.MajorVersion), checked((int)image.MinorVersion), checked((int)image.Build)), Marshal.PtrToStringUni(image.ImageDescription) ?? string.Empty, Marshal.PtrToStringUni(image.ProductType) ?? string.Empty, ReadLanguages(image.Language, image.LanguageCount), checked((int)image.DefaultLanguageIndex));
        }

        return images;
    }

    private static IReadOnlyList<string> ReadLanguages(IntPtr buffer, uint count)
    {
        if (count > 256 || (count > 0 && buffer == IntPtr.Zero)) throw new InvalidDataException("The image language metadata is invalid.");
        var languages = new string[count];
        for (int i = 0; i < languages.Length; i++) languages[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * IntPtr.Size)) ?? string.Empty;
        return languages;
    }

    private static void ThrowIfFailed(int hResult, string operation)
    {
        if (hResult < 0)
        {
            throw new COMException($"{operation} failed with HRESULT 0x{hResult:X8}.", hResult);
        }
    }

    // All fields are required even when not consumed: native array traversal depends on the complete packed stride.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DismImageInfo
    {
        public uint ImageType;
        public uint ImageIndex;
        public IntPtr ImageName;
        public IntPtr ImageDescription;
        public ulong ImageSize;
        public uint Architecture;
        public IntPtr ProductName;
        public IntPtr EditionId;
        public IntPtr InstallationType;
        public IntPtr Hal;
        public IntPtr ProductType;
        public IntPtr ProductSuite;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint Build;
        public uint SpBuild;
        public uint SpLevel;
        public uint Bootable;
        public IntPtr SystemRoot;
        public IntPtr Language;
        public uint LanguageCount;
        public uint DefaultLanguageIndex;
        public IntPtr CustomizedInfo;
    }
}
