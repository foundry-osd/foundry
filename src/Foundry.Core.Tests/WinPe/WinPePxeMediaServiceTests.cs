// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPePxeMediaServiceTests
{
    [Fact]
    public async Task CreateAsync_CopiesTheFourBootFiles()
    {
        using var temp = new TemporaryDirectory();
        WinPeWorkspacePreparationResult workspace = CreateWorkspace(temp.Path, useBootEx: false);
        string output = Path.Combine(temp.Path, "pxe");

        WinPeResult result = await new WinPePxeMediaService().CreateAsync(
            new WinPePxeMediaOptions { PreparedWorkspace = workspace, OutputDirectoryPath = output },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(WinPePxeMediaService.OutputFileNames.Order(StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("wim", File.ReadAllText(Path.Combine(output, "boot.wim")));
        Assert.Equal("sdi", File.ReadAllText(Path.Combine(output, "boot.sdi")));
        Assert.Equal("efi-bcd", File.ReadAllText(Path.Combine(output, "BCD")));
        Assert.Equal("bootmgfw-2011", File.ReadAllText(Path.Combine(output, "bootmgr.efi")));
    }

    [Fact]
    public async Task CreateAsync_WhenBootExIsEnabled_UsesTheCa2023BootManager()
    {
        using var temp = new TemporaryDirectory();
        string output = Path.Combine(temp.Path, "pxe");

        WinPeResult result = await new WinPePxeMediaService().CreateAsync(
            new WinPePxeMediaOptions { PreparedWorkspace = CreateWorkspace(temp.Path, useBootEx: true), OutputDirectoryPath = output },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("bootmgfw-2023", File.ReadAllText(Path.Combine(output, "bootmgr.efi")));
    }

    [Fact]
    public async Task CreateAsync_ReplacesExistingFiles()
    {
        using var temp = new TemporaryDirectory();
        string output = Path.Combine(temp.Path, "pxe");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "boot.wim"), "previous");
        File.WriteAllText(Path.Combine(output, "notes.txt"), "kept");

        WinPeResult result = await new WinPePxeMediaService().CreateAsync(
            new WinPePxeMediaOptions { PreparedWorkspace = CreateWorkspace(temp.Path, useBootEx: false), OutputDirectoryPath = output },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("wim", File.ReadAllText(Path.Combine(output, "boot.wim")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(output, "notes.txt")));
    }

    [Fact]
    public async Task CreateAsync_WhenASourceIsMissing_FailsWithoutWritingFiles()
    {
        using var temp = new TemporaryDirectory();
        WinPeWorkspacePreparationResult workspace = CreateWorkspace(temp.Path, useBootEx: false);
        File.Delete(Path.Combine(workspace.Artifact.WorkingDirectoryPath, "bootbins", "bootmgfw.efi"));
        string output = Path.Combine(temp.Path, "pxe");

        WinPeResult result = await new WinPePxeMediaService().CreateAsync(
            new WinPePxeMediaOptions { PreparedWorkspace = workspace, OutputDirectoryPath = output },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.PxeOutputFailed, result.Error!.Code);
        Assert.Equal(WinPeFailureReasons.ArtifactMissing, result.Error.FailureReason);
        Assert.False(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any());
    }

    [Fact]
    public async Task CreateAsync_WhenSpaceIsInsufficient_FailsWithRequiredBytes()
    {
        using var temp = new TemporaryDirectory();
        string output = Path.Combine(temp.Path, "pxe");
        // "wim" + "sdi" + "efi-bcd" + "bootmgfw-2011"
        const long sourceBytes = 3 + 3 + 7 + 13;

        WinPeResult result = await new WinPePxeMediaService(_ => sourceBytes - 1).CreateAsync(
            new WinPePxeMediaOptions { PreparedWorkspace = CreateWorkspace(temp.Path, useBootEx: false), OutputDirectoryPath = output },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.PxeOutputCapacityInsufficient, result.Error!.Code);
        Assert.Equal((ulong)sourceBytes, result.Error.RequiredBytes);
        Assert.Equal((ulong)(sourceBytes - 1), result.Error.AvailableBytes);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task CreateAsync_WhenCopyFails_KeepsPreviousFilesAndNoPendingFile()
    {
        using var temp = new TemporaryDirectory();
        WinPeWorkspacePreparationResult workspace = CreateWorkspace(temp.Path, useBootEx: false);
        string output = Path.Combine(temp.Path, "pxe");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "boot.wim"), "previous");

        WinPeResult result;
        using (new FileStream(Path.Combine(workspace.Artifact.MediaDirectoryPath, "Boot", "boot.sdi"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await new WinPePxeMediaService().CreateAsync(
                new WinPePxeMediaOptions { PreparedWorkspace = workspace, OutputDirectoryPath = output },
                TestContext.Current.CancellationToken);
        }

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.PxeOutputFailed, result.Error!.Code);
        Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "boot.wim")));
        Assert.Equal(["boot.wim"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    private static WinPeWorkspacePreparationResult CreateWorkspace(string root, bool useBootEx)
    {
        string work = Path.Combine(root, "work");
        string media = Path.Combine(work, "media");
        Write(Path.Combine(media, "sources", "boot.wim"), "wim");
        Write(Path.Combine(media, "Boot", "boot.sdi"), "sdi");
        Write(Path.Combine(media, "Boot", "BCD"), "bios-bcd");
        Write(Path.Combine(media, "EFI", "Microsoft", "Boot", "BCD"), "efi-bcd");
        Write(Path.Combine(media, "bootmgr.efi"), "media-bootmgr");
        Write(Path.Combine(work, "bootbins", "bootmgfw.efi"), "bootmgfw-2011");
        Write(Path.Combine(work, "bootbins", "bootmgfw_EX.efi"), "bootmgfw-2023");

        return new WinPeWorkspacePreparationResult
        {
            Artifact = new WinPeBuildArtifact
            {
                WorkingDirectoryPath = work,
                MediaDirectoryPath = media,
                BootWimPath = Path.Combine(media, "sources", "boot.wim"),
                Architecture = WinPeArchitecture.X64
            },
            Tools = new WinPeToolPaths(),
            UseBootEx = useBootEx
        };
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
