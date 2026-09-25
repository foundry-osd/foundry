// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using Foundry.Utilities.Imaging;
using Xunit;

namespace Foundry.Utilities.Tests;

public sealed class NativeDismImageInfoReaderTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ReadAsync_CopiesPackedArrayWithExactSizesUnicodeAndUndefinedSetupArchitecture()
    {
        using var api = new FakeDismApi
        {
            Images =
            [
                new(9, "Windows 11 Éducation 日本語", "Professional", 26_839_601_777, 9, 10, 0, 26100),
                new(1, "Windows Setup Media", null, 277_641_908, 0xffff, 0, 0, 0),
                new(4, "Windows ARM64", "Enterprise", 9_223_372_036_854_775_808, 12, 10, 1, 22631),
                new(3, "Windows x86", "Core", 4_294_967_297, 0, 6, 3, 9600)
            ]
        };
        using var reader = new NativeDismImageInfoReader(api);

        IReadOnlyList<WindowsImageMetadata> images = await reader.ReadAsync(@"C:\images\install.esd", TestContext.Current.CancellationToken);

        WindowsImageMetadata[] expected =
            [
                new WindowsImageMetadata(9, "Windows 11 Éducation 日本語", "Professional", 26_839_601_777, "x64", new Version(10, 0, 26100), "Description", "WinNT", [], 0),
                new WindowsImageMetadata(1, "Windows Setup Media", "", 277_641_908, "unknown", new Version(0, 0, 0), "Description", "WinNT", [], 0),
                new WindowsImageMetadata(4, "Windows ARM64", "Enterprise", 9_223_372_036_854_775_808, "arm64", new Version(10, 1, 22631), "Description", "WinNT", [], 0),
                new WindowsImageMetadata(3, "Windows x86", "Core", 4_294_967_297, "x86", new Version(6, 3, 9600), "Description", "WinNT", [], 0)
            ];
        Assert.Equal(expected.Length, images.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i] with { Languages = images[i].Languages }, images[i]);
            Assert.Empty(images[i].Languages);
        }
        Assert.Equal(@"C:\images\install.esd", Assert.Single(api.ImagePaths));
        Assert.Equal(1, api.DeleteCalls);
        Assert.Empty(api.OutstandingBuffers);
    }

    [Fact]
    public async Task ReadAsync_InitializesOnceAndShutsDownOnceAfterMultipleReads()
    {
        using var api = new FakeDismApi();
        var reader = new NativeDismImageInfoReader(api);

        await reader.ReadAsync("first.esd", TestContext.Current.CancellationToken);
        await reader.ReadAsync("second.wim", TestContext.Current.CancellationToken);
        reader.Dispose();
        reader.Dispose();

        Assert.Equal(["initialize", "read", "delete", "read", "delete", "shutdown"], api.Calls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ReadAsync("third.wim", TestContext.Current.CancellationToken));
        Assert.Equal(2, api.ImagePaths.Count);
    }

    [Fact]
    public async Task ReadAsync_PreCanceledRequestAndUnusedDisposeDoNotInitializeNativeDism()
    {
        using var api = new FakeDismApi();
        var reader = new NativeDismImageInfoReader(api);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync("install.esd", cancellation.Token));
        reader.Dispose();

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task ReadAsync_InitializationFailurePreservesHResultWithoutReadingOrShuttingDown()
    {
        using var api = new FakeDismApi { InitializeResult = unchecked((int)0x80070005) };
        var reader = new NativeDismImageInfoReader(api);

        COMException error = await Assert.ThrowsAsync<COMException>(() => reader.ReadAsync("install.esd", TestContext.Current.CancellationToken));
        reader.Dispose();

        Assert.Equal(unchecked((int)0x80070005), error.HResult);
        Assert.Equal(["initialize"], api.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ReadFailurePreservesHResultAndReleasesAnyReturnedBuffer(bool returnsBuffer)
    {
        using var api = new FakeDismApi
        {
            ReadResult = unchecked((int)0x8007000D),
            ReturnBuffer = returnsBuffer,
            DeleteResult = unchecked((int)0x80004005)
        };
        using var reader = new NativeDismImageInfoReader(api);

        COMException error = await Assert.ThrowsAsync<COMException>(() => reader.ReadAsync("invalid.esd", TestContext.Current.CancellationToken));

        Assert.Equal(unchecked((int)0x8007000D), error.HResult);
        Assert.Equal(returnsBuffer ? 1 : 0, api.DeleteCalls);
        Assert.Empty(api.OutstandingBuffers);
    }

    [Fact]
    public async Task ReadAsync_ConversionFailureStillReleasesNativeBuffer()
    {
        using var api = new FakeDismApi { Images = [new(uint.MaxValue, "Invalid index", "Professional", 1, 9, 10, 0, 26100)] };
        using var reader = new NativeDismImageInfoReader(api);

        await Assert.ThrowsAsync<OverflowException>(() => reader.ReadAsync("invalid.esd", TestContext.Current.CancellationToken));

        Assert.Equal(1, api.DeleteCalls);
        Assert.Empty(api.OutstandingBuffers);
    }

    [Fact]
    public async Task ReadAsync_NullBufferWithNonzeroCountFailsWithoutDereferencing()
    {
        using var api = new FakeDismApi { ReturnBuffer = false, CountOverride = 1 };
        using var reader = new NativeDismImageInfoReader(api);

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("invalid.esd", TestContext.Current.CancellationToken));

        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public async Task ReadAsync_EmptyNativeResultReturnsEmptyMetadata()
    {
        using var api = new FakeDismApi { ReturnBuffer = false, CountOverride = 0 };
        using var reader = new NativeDismImageInfoReader(api);

        Assert.Empty(await reader.ReadAsync("empty.esd", TestContext.Current.CancellationToken));
        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public async Task ReadAsync_DeleteFailurePreservesHResult()
    {
        using var api = new FakeDismApi { DeleteResult = unchecked((int)0x80004005) };
        using var reader = new NativeDismImageInfoReader(api);

        COMException error = await Assert.ThrowsAsync<COMException>(() => reader.ReadAsync("install.esd", TestContext.Current.CancellationToken));

        Assert.Equal(unchecked((int)0x80004005), error.HResult);
        Assert.Equal(1, api.DeleteCalls);
    }

    [Fact]
    public async Task Dispose_ShutdownFailurePreservesHResultAndDoesNotRepeatShutdown()
    {
        using var api = new FakeDismApi { ShutdownResult = unchecked((int)0x80004005) };
        var reader = new NativeDismImageInfoReader(api);
        await reader.ReadAsync("install.esd", TestContext.Current.CancellationToken);

        COMException error = Assert.Throws<COMException>(reader.Dispose);
        reader.Dispose();

        Assert.Equal(unchecked((int)0x80004005), error.HResult);
        Assert.Equal(1, api.Calls.Count(call => call == "shutdown"));
    }

    [Fact]
    public async Task ReadAsync_CancellationDuringNativeReadWaitsForReturnAndBufferRelease()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        using var api = new FakeDismApi
        {
            DuringRead = () =>
            {
                entered.Set();
                Assert.True(release.Wait(TestTimeout, TestContext.Current.CancellationToken));
            }
        };
        using var reader = new NativeDismImageInfoReader(api);
        var returned = new TaskCompletionSource<Task<IReadOnlyList<WindowsImageMetadata>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task launch = Task.Run(() => returned.SetResult(reader.ReadAsync("install.esd", cancellation.Token)), TestContext.Current.CancellationToken);
        Task<IReadOnlyList<WindowsImageMetadata>>? read = null;
        try
        {
            read = await returned.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
            Assert.True(entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
            cancellation.Cancel();

            Assert.False(read.IsCompleted);
            Assert.Equal(0, api.DeleteCalls);
        }
        finally
        {
            release.Set();
            await launch.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read!.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(1, api.DeleteCalls);
        Assert.Empty(api.OutstandingBuffers);
    }

    [Fact]
    public async Task ReadAsync_ConcurrentReadsAndDisposalDoNotOverlapNativeOwnership()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var api = new FakeDismApi
        {
            DuringRead = () =>
            {
                entered.Set();
                Assert.True(release.Wait(TestTimeout, TestContext.Current.CancellationToken));
            }
        };
        var reader = new NativeDismImageInfoReader(api);
        Task<IReadOnlyList<WindowsImageMetadata>> first = reader.ReadAsync("first.esd", TestContext.Current.CancellationToken);
        Task<IReadOnlyList<WindowsImageMetadata>>? second = null;
        Task? disposal = null;
        try
        {
            Assert.True(entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
            second = reader.ReadAsync("second.esd", TestContext.Current.CancellationToken);
            disposal = Task.Run(reader.Dispose, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(disposal.IsCompleted);
            Assert.Equal(["initialize", "read"], api.Calls);
        }
        finally
        {
            release.Set();
        }

        await first.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        Exception? secondFailure = await Record.ExceptionAsync(() => second!.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
        Assert.True(secondFailure is null or ObjectDisposedException);
        await disposal!.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        Assert.Empty(api.OutstandingBuffers);
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    private sealed record NativeImage(uint Index, string? Name, string? Edition, ulong Size, uint Architecture, uint Major, uint Minor, uint Build);

    private sealed class FakeDismApi : IDismImageInfoApi, IDisposable
    {
        public NativeImage[] Images { get; init; } = [new(1, "Windows 11 Pro", "Professional", 26_839_601_777, 9, 10, 0, 26100)];
        public List<string> Calls { get; } = [];
        public List<string> ImagePaths { get; } = [];
        public Dictionary<IntPtr, NativeImageBuffer> OutstandingBuffers { get; } = [];
        public int InitializeResult { get; init; }
        public int ReadResult { get; init; }
        public int DeleteResult { get; init; }
        public int ShutdownResult { get; init; }
        public bool ReturnBuffer { get; init; } = true;
        public uint? CountOverride { get; init; }
        public Action? DuringRead { get; init; }
        public int DeleteCalls { get; private set; }

        public int Initialize()
        {
            Calls.Add("initialize");
            return InitializeResult;
        }

        public int GetImageInfo(string imagePath, out IntPtr imageInfo, out uint count)
        {
            Calls.Add("read");
            ImagePaths.Add(imagePath);
            imageInfo = IntPtr.Zero;
            count = CountOverride ?? (uint)Images.Length;
            if (ReturnBuffer)
            {
                var buffer = new NativeImageBuffer(Images);
                imageInfo = buffer.Pointer;
                OutstandingBuffers.Add(imageInfo, buffer);
            }

            DuringRead?.Invoke();
            return ReadResult;
        }

        public int Delete(IntPtr imageInfo)
        {
            Calls.Add("delete");
            DeleteCalls++;
            Assert.True(OutstandingBuffers.Remove(imageInfo, out NativeImageBuffer? buffer));
            buffer.Dispose();
            return DeleteResult;
        }

        public int Shutdown()
        {
            Assert.Empty(OutstandingBuffers);
            Calls.Add("shutdown");
            return ShutdownResult;
        }

        public void Dispose()
        {
            foreach (NativeImageBuffer buffer in OutstandingBuffers.Values)
            {
                buffer.Dispose();
            }

            OutstandingBuffers.Clear();
        }
    }

    private sealed class NativeImageBuffer : IDisposable
    {
        private readonly List<IntPtr> _strings = [];
        public IntPtr Pointer { get; }

        public NativeImageBuffer(NativeImage[] images)
        {
            // These offsets are hand-derived from DismAPI.h, independent of the production struct.
            int stride = IntPtr.Size == 8 ? 140 : 96;
            int sizeOffset = IntPtr.Size == 8 ? 24 : 16;
            int architectureOffset = IntPtr.Size == 8 ? 32 : 24;
            int productOffset = IntPtr.Size == 8 ? 36 : 28;
            int versionOffset = IntPtr.Size == 8 ? 84 : 52;
            int rootOffset = IntPtr.Size == 8 ? 108 : 76;
            Pointer = Marshal.AllocHGlobal(stride * images.Length);
            Marshal.Copy(new byte[stride * images.Length], 0, Pointer, stride * images.Length);
            for (int index = 0; index < images.Length; index++)
            {
                NativeImage image = images[index];
                IntPtr entry = IntPtr.Add(Pointer, stride * index);
                Marshal.WriteInt32(entry, 0, 0);
                Marshal.WriteInt32(entry, 4, unchecked((int)image.Index));
                WriteString(entry, 8, image.Name);
                WriteString(entry, 8 + IntPtr.Size, "Description");
                Marshal.WriteInt64(entry, sizeOffset, unchecked((long)image.Size));
                Marshal.WriteInt32(entry, architectureOffset, unchecked((int)image.Architecture));
                WriteString(entry, productOffset, "Microsoft Windows Operating System");
                WriteString(entry, productOffset + IntPtr.Size, image.Edition);
                WriteString(entry, productOffset + IntPtr.Size * 2, "Client");
                WriteString(entry, productOffset + IntPtr.Size * 3, "");
                WriteString(entry, productOffset + IntPtr.Size * 4, "WinNT");
                WriteString(entry, productOffset + IntPtr.Size * 5, "Terminal Server");
                Marshal.WriteInt32(entry, versionOffset, unchecked((int)image.Major));
                Marshal.WriteInt32(entry, versionOffset + 4, unchecked((int)image.Minor));
                Marshal.WriteInt32(entry, versionOffset + 8, unchecked((int)image.Build));
                Marshal.WriteInt32(entry, versionOffset + 12, 1234);
                Marshal.WriteInt32(entry, versionOffset + 16, 0);
                Marshal.WriteInt32(entry, versionOffset + 20, 1);
                WriteString(entry, rootOffset, "WINDOWS");
                // No languages or customized information: their pointer/count fields remain zero.
            }
        }

        private void WriteString(IntPtr entry, int offset, string? value)
        {
            if (value is null)
            {
                return;
            }

            IntPtr text = Marshal.StringToHGlobalUni(value);
            _strings.Add(text);
            Marshal.WriteIntPtr(entry, offset, text);
        }

        public void Dispose()
        {
            foreach (IntPtr text in _strings)
            {
                Marshal.FreeHGlobal(text);
            }

            Marshal.FreeHGlobal(Pointer);
        }
    }
}
