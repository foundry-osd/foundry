// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class NativeWinPeMountedImageInventoryTests
{
    [Fact]
    public void Read_CopiesPackedUnicodePathsForEveryRegisteredMount()
    {
        using var api = new FakeApi(
            new WinPeMountedImage(@"C:\作業\mount", @"C:\作業\boot.wim"),
            new WinPeMountedImage(@"D:\réparation\install-mount", @"D:\réparation\install.wim"));
        using var inventory = new NativeWinPeMountedImageInventory(api);

        IReadOnlyList<WinPeMountedImage> images = inventory.Read();

        Assert.Equal(api.Images, images);
        Assert.Equal(1, api.DeleteCalls);
        Assert.Equal(IntPtr.Zero, api.Buffer);
    }

    [Fact]
    public void Read_InitializesOnceAndRefreshesInventoryBeforeEveryRead()
    {
        using var api = new FakeApi();
        var inventory = new NativeWinPeMountedImageInventory(api);

        Assert.Empty(inventory.Read());
        Assert.Empty(inventory.Read());
        inventory.Dispose();
        inventory.Dispose();

        Assert.Equal(1, api.InitializeCalls);
        Assert.Equal(2, api.QueryCalls);
        Assert.Equal(0, api.DeleteCalls);
        Assert.Equal(1, api.ShutdownCalls);
        Assert.Throws<ObjectDisposedException>(() => inventory.Read());
    }

    [Fact]
    public void Read_WhenInitializationFails_DoesNotQueryOrShutDownUnownedApi()
    {
        using var api = new FakeApi { InitializeResult = unchecked((int)0x80070005) };
        var inventory = new NativeWinPeMountedImageInventory(api);

        COMException error = Assert.Throws<COMException>(() => inventory.Read());
        inventory.Dispose();

        Assert.Contains("DismInitialize", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, api.QueryCalls);
        Assert.Equal(0, api.ShutdownCalls);
    }

    [Fact]
    public void Dispose_WhenNeverRead_DoesNotInitializeOrShutDownApi()
    {
        using var api = new FakeApi();
        new NativeWinPeMountedImageInventory(api).Dispose();

        Assert.Equal(0, api.InitializeCalls);
        Assert.Equal(0, api.ShutdownCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(uint.MaxValue)]
    public void Read_WhenCountHasNoBuffer_RejectsInventory(uint count)
    {
        using var api = new FakeApi { ReturnedCount = count };
        using var inventory = new NativeWinPeMountedImageInventory(api);

        Assert.Throws<InvalidDataException>(() => inventory.Read());
    }

    [Fact]
    public void Read_WhenNativeCountOverflows_RejectsInventoryBeforeMarshalingAndReleasesBuffer()
    {
        using var api = new FakeApi(new WinPeMountedImage(@"C:\mount", @"C:\image.wim"))
        {
            ReturnedCount = uint.MaxValue
        };
        using var inventory = new NativeWinPeMountedImageInventory(api);

        Assert.Throws<OverflowException>(() => inventory.Read());
        Assert.Equal(1, api.DeleteCalls);
    }

    [Theory]
    [InlineData("", @"C:\image.wim")]
    [InlineData(@"C:\mount", "")]
    public void Read_WhenNativePathIsMissing_RejectsInventoryAndReleasesBuffer(string mountPath, string imagePath)
    {
        using var api = new FakeApi(new WinPeMountedImage(mountPath, imagePath));
        using var inventory = new NativeWinPeMountedImageInventory(api);

        Assert.Throws<InvalidDataException>(() => inventory.Read());
        Assert.Equal(1, api.DeleteCalls);
    }

    [Fact]
    public void Read_WhenQueryAndReleaseFail_PreservesQueryFailureAndReleasesBuffer()
    {
        using var api = new FakeApi(new WinPeMountedImage(@"C:\mount", @"C:\image.wim"))
        {
            QueryResult = unchecked((int)0x80070005),
            DeleteResult = unchecked((int)0x80004005)
        };
        using var inventory = new NativeWinPeMountedImageInventory(api);

        COMException error = Assert.Throws<COMException>(() => inventory.Read());

        Assert.Contains("DismGetMountedImageInfo", error.Message, StringComparison.Ordinal);
        Assert.Equal(api.QueryResult, error.HResult);
        Assert.Equal(1, api.DeleteCalls);
    }

    [Fact]
    public void Read_WhenReleaseFails_DoesNotReturnTrustedInventory()
    {
        using var api = new FakeApi(new WinPeMountedImage(@"C:\mount", @"C:\image.wim"))
        {
            DeleteResult = unchecked((int)0x80004005)
        };
        using var inventory = new NativeWinPeMountedImageInventory(api);

        COMException error = Assert.Throws<COMException>(() => inventory.Read());

        Assert.Contains("DismDelete", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, api.DeleteCalls);
    }

    private sealed class FakeApi(params WinPeMountedImage[] images) : IWinPeMountedImageApi, IDisposable
    {
        private readonly List<IntPtr> _paths = [];
        internal WinPeMountedImage[] Images { get; } = images;
        internal IntPtr Buffer { get; private set; }
        internal int InitializeResult { get; init; }
        internal int QueryResult { get; init; }
        internal int DeleteResult { get; init; }
        internal uint? ReturnedCount { get; init; }
        internal int InitializeCalls { get; private set; }
        internal int QueryCalls { get; private set; }
        internal int DeleteCalls { get; private set; }
        internal int ShutdownCalls { get; private set; }

        public int Initialize()
        {
            InitializeCalls++;
            return InitializeResult;
        }

        public int GetMountedImageInfo(out IntPtr imageInfo, out uint count)
        {
            QueryCalls++;
            count = ReturnedCount ?? (uint)Images.Length;
            if (Images.Length == 0)
            {
                imageInfo = IntPtr.Zero;
                return QueryResult;
            }

            // DismAPI.h uses pack(1): two native pointers followed by three 32-bit fields.
            // Hand-write offsets independently of the production marshaling structure.
            int stride = 2 * IntPtr.Size + 12;
            Buffer = Marshal.AllocHGlobal(stride * Images.Length);
            for (int index = 0; index < Images.Length; index++)
            {
                IntPtr entry = IntPtr.Add(Buffer, index * stride);
                Marshal.WriteIntPtr(entry, AllocatePath(Images[index].MountPath));
                Marshal.WriteIntPtr(entry, IntPtr.Size, AllocatePath(Images[index].ImagePath));
                Marshal.WriteInt32(entry, 2 * IntPtr.Size, index + 1);
                Marshal.WriteInt32(entry, 2 * IntPtr.Size + 4, 0);
                Marshal.WriteInt32(entry, 2 * IntPtr.Size + 8, index + 1);
            }

            imageInfo = Buffer;
            return QueryResult;
        }

        public int Delete(IntPtr imageInfo)
        {
            Assert.Equal(Buffer, imageInfo);
            DeleteCalls++;
            Release();
            return DeleteResult;
        }

        public int Shutdown()
        {
            ShutdownCalls++;
            return 0;
        }

        public void Dispose() => Release();

        private IntPtr AllocatePath(string path)
        {
            IntPtr pointer = string.IsNullOrEmpty(path) ? IntPtr.Zero : Marshal.StringToHGlobalUni(path);
            _paths.Add(pointer);
            return pointer;
        }

        private void Release()
        {
            foreach (IntPtr pointer in _paths)
            {
                Marshal.FreeHGlobal(pointer);
            }

            _paths.Clear();
            Marshal.FreeHGlobal(Buffer);
            Buffer = IntPtr.Zero;
        }
    }
}
