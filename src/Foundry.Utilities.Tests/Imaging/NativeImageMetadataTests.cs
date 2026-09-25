// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Imaging;
using Xunit;

namespace Foundry.Utilities.Tests.Imaging;

public sealed class NativeImageMetadataTests
{
    [Fact]
    public async Task MetadataCopiesLanguagesAndExactUnknownProductWithoutNativePointers()
    {
        using var api = new MetadataApi();
        using var reader = new NativeDismImageInfoReader(api);
        WindowsImageMetadata image = Assert.Single(await reader.ReadAsync("custom.wim", TestContext.Current.CancellationToken));
        Assert.Equal(7, image.Index);
        Assert.Equal("Custom 日本語", image.Name);
        Assert.Equal("x86", image.Architecture);
        Assert.Equal("Uncatalogued", image.EditionId);
        Assert.Equal(["en-US", "ja-JP"], image.Languages);
        Assert.Equal(1, image.DefaultLanguageIndex);
        Assert.Equal(5_000_000_000ul, image.ImageSize);
        Assert.Equal(1, api.DeleteCalls);
    }

    [Fact]
    public async Task InvalidLanguageCountStillReleasesTheNativeAllocation()
    {
        using var api = new MetadataApi { LanguageCount = 257 };
        using var reader = new NativeDismImageInfoReader(api);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync("custom.wim", TestContext.Current.CancellationToken));
        Assert.Equal(1, api.DeleteCalls);
    }

    private sealed class MetadataApi : IDismImageInfoApi, IDisposable
    {
        private readonly List<IntPtr> allocations = [];
        public uint LanguageCount { get; init; } = 2;
        public int DeleteCalls { get; private set; }
        public int Initialize() => 0;
        public int Shutdown() => 0;
        public int GetImageInfo(string imagePath, out IntPtr imageInfo, out uint count)
        {
            IntPtr languages = Allocate(2 * IntPtr.Size);
            Marshal.WriteIntPtr(languages, 0, Text("en-US"));
            Marshal.WriteIntPtr(languages, IntPtr.Size, Text("ja-JP"));
            var native = new Image
            {
                Index = 7,
                Name = Text("Custom 日本語"),
                Edition = Text("Uncatalogued"),
                Size = 5_000_000_000,
                Architecture = 0,
                Major = 6,
                Minor = 1,
                Build = 7601,
                Languages = languages,
                LanguageCount = LanguageCount,
                DefaultLanguage = 1
            };
            imageInfo = Allocate(Marshal.SizeOf<Image>());
            Marshal.StructureToPtr(native, imageInfo, false);
            count = 1;
            return 0;
        }
        public int Delete(IntPtr imageInfo) { DeleteCalls++; Dispose(); return 0; }
        public void Dispose()
        {
            foreach (IntPtr pointer in allocations) Marshal.FreeHGlobal(pointer);
            allocations.Clear();
        }
        private IntPtr Allocate(int length)
        {
            IntPtr pointer = Marshal.AllocHGlobal(length);
            allocations.Add(pointer);
            return pointer;
        }
        private IntPtr Text(string text)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(text);
            allocations.Add(pointer);
            return pointer;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Image
    {
        public uint Type;
        public uint Index;
        public IntPtr Name;
        public IntPtr Description;
        public ulong Size;
        public uint Architecture;
        public IntPtr ProductName;
        public IntPtr Edition;
        public IntPtr InstallationType;
        public IntPtr Hal;
        public IntPtr ProductType;
        public IntPtr ProductSuite;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint SpBuild;
        public uint SpLevel;
        public uint Bootable;
        public IntPtr SystemRoot;
        public IntPtr Languages;
        public uint LanguageCount;
        public uint DefaultLanguage;
        public IntPtr CustomizedInfo;
    }
}
