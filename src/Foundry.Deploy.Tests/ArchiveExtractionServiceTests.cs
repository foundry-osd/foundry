using System.Runtime.InteropServices;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Tests;

public sealed class ArchiveExtractionServiceTests
{
    [Fact]
    public void ResolveSevenZipExecutablePath_WhenWinPeToolingExists_UsesProvisionedRuntimeTool()
    {
        string root = CreateTempDirectory();
        try
        {
            string systemDirectory = Path.Combine(root, "X", "Windows", "System32");
            string expectedPath = Path.Combine(
                root,
                "X",
                "Foundry",
                "Tools",
                "7zip",
                ExpectedRuntimeFolder,
                "7za.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
            File.WriteAllText(expectedPath, "7za");

            string resolvedPath = ArchiveExtractionService.ResolveSevenZipExecutablePath(systemDirectory);

            Assert.Equal(expectedPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSevenZipExecutablePath_WhenOnlySystem32CopyExists_Throws()
    {
        string root = CreateTempDirectory();
        try
        {
            string systemDirectory = Path.Combine(root, "X", "Windows", "System32");
            string system32Path = Path.Combine(systemDirectory, "7za.exe");
            Directory.CreateDirectory(systemDirectory);
            File.WriteAllText(system32Path, "7za");

            Assert.Throws<FileNotFoundException>(
                () => ArchiveExtractionService.ResolveSevenZipExecutablePath(systemDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSevenZipExecutablePath_WhenOnlyAppLocalAssetExists_Throws()
    {
        string root = CreateTempDirectory();
        try
        {
            string baseDirectory = Path.Combine(root, "app");
            string systemDirectory = Path.Combine(root, "X", "Windows", "System32");
            string appLocalPath = Path.Combine(baseDirectory, "Assets", "7z", ExpectedRuntimeFolder, "7za.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(appLocalPath)!);
            File.WriteAllText(appLocalPath, "7za");

            Assert.Throws<FileNotFoundException>(
                () => ArchiveExtractionService.ResolveSevenZipExecutablePath(systemDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSevenZipExecutablePath_WhenNoCandidateExists_ReportsProvisionedToolPath()
    {
        string root = CreateTempDirectory();
        try
        {
            string systemDirectory = Path.Combine(root, "X", "Windows", "System32");

            FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
                () => ArchiveExtractionService.ResolveSevenZipExecutablePath(systemDirectory));

            Assert.Contains(@"Foundry\Tools\7zip", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ExpectedRuntimeFolder => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "arm64"
        : "x64";

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
