// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection.PortableExecutable;

namespace Foundry.Core.Tests.TestUtilities;

internal static class PortableExecutableFixture
{
    internal static void Write(string path, Machine machine, bool managed = false, CorFlags flags = CorFlags.ILOnly)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new byte[1024];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream);
        bool pe32 = machine == Machine.I386;
        writer.Write((ushort)0x5A4D);
        stream.Position = 0x3C;
        writer.Write(0x80);
        stream.Position = 0x80;
        writer.Write(0x00004550);
        writer.Write((ushort)machine);
        writer.Write((ushort)1);
        stream.Position = 0x94;
        writer.Write((ushort)(pe32 ? 224 : 240));
        writer.Write((ushort)Characteristics.ExecutableImage);
        writer.Write((ushort)(pe32 ? 0x10B : 0x20B));
        stream.Position = 0x98 + 32;
        writer.Write(4096);
        writer.Write(512);
        stream.Position = 0x98 + 56;
        writer.Write(8192);
        writer.Write(512);
        stream.Position = 0x98 + (pe32 ? 92 : 108);
        writer.Write(16);
        if (managed)
        {
            stream.Position = 0x98 + (pe32 ? 96 : 112) + 14 * 8;
            writer.Write(4096);
            writer.Write(72);
            stream.Position = 512;
            writer.Write(72);
            writer.Write((ushort)2);
            writer.Write((ushort)5);
            writer.Write(4176);
            writer.Write(16);
            stream.Position = 528;
            writer.Write((int)flags);
        }
        stream.Position = pe32 ? 0x178 : 0x188;
        writer.Write(".text\0\0\0"u8);
        writer.Write(512);
        writer.Write(4096);
        writer.Write(512);
        writer.Write(512);
        File.WriteAllBytes(path, bytes);
    }
}
