// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection.PortableExecutable;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeExecutableArchitectureTests
{
    [Theory]
    [InlineData(Machine.Amd64, WinPeArchitecture.X64)]
    [InlineData(Machine.Arm64, WinPeArchitecture.Arm64)]
    public void ValidateNative_RequiresMatchingMachine(Machine machine, WinPeArchitecture target)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "tool.exe");
        PortableExecutableFixture.Write(path, machine);
        Assert.Equal(machine, WinPeExecutableArchitecture.ReadMachine(path));
        WinPeExecutableArchitecture.ValidateNative(path, target);
        Assert.Throws<InvalidDataException>(() => WinPeExecutableArchitecture.ValidateNative(path,
            target == WinPeArchitecture.X64 ? WinPeArchitecture.Arm64 : WinPeArchitecture.X64));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(600)]
    public void ValidateNative_RejectsTruncatedHeadersAndSections(int length)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "tool.exe");
        PortableExecutableFixture.Write(path, Machine.Amd64);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
        }
        Assert.Throws<InvalidDataException>(() => WinPeExecutableArchitecture.ValidateNative(path, WinPeArchitecture.X64));
    }

    [Fact]
    public void ValidateRuntimeLibrary_AllowsAnyCpuButRejectsRequired32BitAndManagedApphosts()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "assembly.dll");
        PortableExecutableFixture.Write(path, Machine.I386, managed: true);
        WinPeExecutableArchitecture.ValidateRuntimeLibrary(path, WinPeArchitecture.Arm64);
        Assert.Throws<InvalidDataException>(() => WinPeExecutableArchitecture.ValidateNative(path, WinPeArchitecture.X64));
        PortableExecutableFixture.Write(path, Machine.I386, managed: true, CorFlags.ILOnly | CorFlags.Requires32Bit);
        Assert.Throws<InvalidDataException>(() => WinPeExecutableArchitecture.ValidateRuntimeLibrary(path, WinPeArchitecture.X64));
    }
}
