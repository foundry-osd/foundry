// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Runtime;
using Foundry.Utilities.Tests.IO;

namespace Foundry.Utilities.Tests.Runtime;

public sealed class PackageValidationTests
{
    [Fact]
    public void ApphostDirectoryIsIndependentOfBundleExtractionDirectory()
    {
        using TemporaryDirectory package = new();
        string executable = Path.Combine(package.Path, "Foundry.Deploy.exe");
        File.WriteAllBytes(executable, []);
        Assert.Equal(package.Path, PackageValidation.ResolvePackageDirectory(executable, "Foundry.Deploy.exe"));
        Assert.Throws<InvalidDataException>(() => PackageValidation.ResolvePackageDirectory(executable, "dotnet.exe"));
    }

    [Theory]
    [InlineData(PackageValidationStage.Resources, "package_resources_failed")]
    [InlineData(PackageValidationStage.EmbeddedAssets, "package_embedded_assets_failed")]
    [InlineData(PackageValidationStage.PackagedRecords, "package_records_failed")]
    [InlineData(PackageValidationStage.NativeLibraries, "package_native_libraries_failed")]
    public void StageFailureReportsOnlyFixedCode(PackageValidationStage stage, string code)
    {
        using StringWriter output = new();
        Assert.Equal(3, PackageValidation.Run(Arguments(), "1", Architecture.X64, Architecture.X64,
            _ => PackageValidation.CheckStage(stage, () => throw new IOException("private path")), output));
        Assert.Contains(code, output.ToString());
        Assert.DoesNotContain("private", output.ToString());
    }

    [Theory]
    [InlineData("--offline")]
    [InlineData("--expected-version")]
    [InlineData("--validate-package")]
    public void InvalidArgumentsNeverInvokeValidation(string extra)
    {
        bool invoked = false;
        string[] args = ["--validate-package", "--expected-version", "1", "--expected-runtime", "win-x64", extra];
        Assert.Equal(2, PackageValidation.Run(args, "1", Architecture.X64, Architecture.X64, _ => invoked = true, TextWriter.Null));
        Assert.False(invoked);
    }

    [Theory]
    [InlineData("2", Architecture.X64)]
    [InlineData("1", Architecture.X86)]
    public void MismatchedIdentityStopsBeforeValidation(string version, Architecture architecture)
    {
        bool invoked = false;
        Assert.Equal(3, PackageValidation.Run(Arguments(), version, architecture, Architecture.X64, _ => invoked = true, TextWriter.Null));
        Assert.False(invoked);
    }

    [Fact]
    public void SuccessfulValidationAndFailureEmitOnlySafeCodes()
    {
        bool invoked = false;
        Assert.Equal(0, PackageValidation.Run(Arguments(), "1", Architecture.X64, Architecture.X64, _ => invoked = true, TextWriter.Null));
        Assert.True(invoked);
        using StringWriter output = new();
        Assert.Equal(3, PackageValidation.Run(Arguments(), "1", Architecture.X64, Architecture.X64,
            _ => throw new IOException("private package path"), output));
        Assert.Contains("package_validation_failed", output.ToString());
        Assert.DoesNotContain("private", output.ToString());
    }

    [Fact]
    public void NativeSearchCannotUseUnrelatedDirectory()
    {
        using TemporaryDirectory package = new();
        using TemporaryDirectory unrelated = new();
        WriteNativeSet(unrelated.Path);
        Assert.Throws<InvalidDataException>(() => PackageValidation.ValidateNativeLibraries(package.Path, null, unrelated.Path,
            Architecture.X64, _ => throw new InvalidOperationException("must not load"), _ => { }));
    }

    [Fact]
    public void OwnedExtractionLoadsAbsolutePathsAndReleasesHandlesAfterFailure()
    {
        using TemporaryDirectory package = new();
        using TemporaryDirectory extraction = new();
        string root = Path.Combine(extraction.Path, "application", "bundle");
        Directory.CreateDirectory(root);
        WriteNativeSet(root);
        List<nint> freed = [];
        int count = 0;
        Assert.Throws<DllNotFoundException>(() => PackageValidation.ValidateNativeLibraries(package.Path, extraction.Path, root,
            Architecture.X64, path =>
            {
                Assert.Equal(root, Path.GetDirectoryName(path));
                if (++count == 3) throw new DllNotFoundException();
                return count;
            }, freed.Add));
        Assert.Equal(new nint[] { 2, 1 }, freed);
    }

    [Fact]
    public void WrongMachineBlocksEveryLoad()
    {
        using TemporaryDirectory package = new();
        WriteNativeSet(package.Path, 0x14c);
        Assert.Throws<InvalidDataException>(() => PackageValidation.ValidateNativeLibraries(package.Path, null, package.Path,
            Architecture.X64, _ => throw new InvalidOperationException("must not load"), _ => { }));
    }

    private static string[] Arguments() => ["--validate-package", "--expected-version", "1", "--expected-runtime", "win-x64"];
    private static void WriteNativeSet(string root, ushort machine = 0x8664)
    {
        foreach (string name in new[] { "vcruntime140_cor3.dll", "D3DCompiler_47_cor3.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "PenImc_cor3.dll" })
        {
            using FileStream file = File.Create(Path.Combine(root, name));
            using BinaryWriter writer = new(file);
            file.SetLength(512);
            writer.Write((ushort)0x5a4d);
            file.Position = 0x3c;
            writer.Write(0x80);
            file.Position = 0x80;
            writer.Write(0x4550);
            writer.Write(machine);
            file.Position = 0x94;
            writer.Write((ushort)240);
            writer.Write((ushort)0x2002);
            writer.Write((ushort)0x20b);
        }
    }
}
