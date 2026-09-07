// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection.PortableExecutable;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Checks the machine of installed or authenticated payloads without loading executable code.
/// These checks establish architecture compatibility, not publisher trust or boot qualification.
/// </summary>
internal static class WinPeExecutableArchitecture
{
    /// <summary>Reads a complete PE image header and rejects truncated section data.</summary>
    internal static Machine ReadMachine(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        ValidateHeaders(reader, stream.Length, path);
        return reader.PEHeaders.CoffHeader.Machine;
    }

    /// <summary>Requires a native executable or library for the target machine.</summary>
    internal static void ValidateNative(string path, WinPeArchitecture target)
    {
        Validate(path, target, allowManaged: false);
    }

    /// <summary>Allows portable IL-only assemblies while requiring native and mixed code to match the target.</summary>
    internal static void ValidateRuntimeLibrary(string path, WinPeArchitecture target)
    {
        Validate(path, target, allowManaged: true);
    }

    private static void Validate(string path, WinPeArchitecture target, bool allowManaged)
    {
        Machine expected = target switch
        {
            WinPeArchitecture.X64 => Machine.Amd64,
            WinPeArchitecture.Arm64 => Machine.Arm64,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            ValidateHeaders(reader, stream.Length, path);
            Machine actual = reader.PEHeaders.CoffHeader.Machine;
            CorHeader? managedHeader = reader.PEHeaders.CorHeader;
            bool portableAssembly = allowManaged && managedHeader is not null && actual == Machine.I386 &&
                managedHeader.Flags.HasFlag(CorFlags.ILOnly) && !managedHeader.Flags.HasFlag(CorFlags.Requires32Bit);
            if ((!allowManaged && managedHeader is not null) || (!portableAssembly && actual != expected))
            {
                throw new InvalidDataException($"Executable '{path}' is incompatible with {target}: machine={actual}, managed={managedHeader is not null}.");
            }
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidDataException($"Executable '{path}' is not a valid PE image.", ex);
        }
    }

    private static void ValidateHeaders(PEReader reader, long length, string path)
    {
        if (reader.PEHeaders.PEHeader is null || reader.PEHeaders.PEHeader.SizeOfHeaders > length ||
            !reader.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.ExecutableImage) ||
            reader.PEHeaders.SectionHeaders.Length == 0 ||
            reader.PEHeaders.SectionHeaders.Any(section => section.PointerToRawData < 0 || section.SizeOfRawData < 0 ||
                (long)section.PointerToRawData + section.SizeOfRawData > length))
        {
            throw new InvalidDataException($"Executable '{path}' has an invalid or truncated PE image.");
        }
    }
}
