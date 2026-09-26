// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeCustomImageIsoTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-ordered-iso-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Capacity_IncludesFinalAtomicCopyEvenWhenItSharesTheStagingVolume()
    {
        IReadOnlyDictionary<string, long> budgets = WinPeCustomImageIsoMastering.GetSpaceRequirements(
            @"C:\staging", @"C:\scratch\temporary.iso", @"C:\outputs\foundry.iso", 100);

        Assert.Equal(300, budgets[@"C:\"]);
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public void Capacity_UncShareReservesBothPendingCopies(long shareAvailableBytes, bool succeeds)
    {
        const string share = @"\\server\media";
        var queriedRoots = new List<string>();
        var queriedOutputs = new List<string>();
        Action validate = () => WinPeCustomImageIsoMastering.ValidateCapacity(@"C:\staging",
            share + @"\scratch\temporary.iso", share + @"\outputs\foundry.iso", 100,
            volume =>
            {
                queriedRoots.Add(volume);
                return volume == share ? shareAvailableBytes : 100;
            },
            output =>
            {
                queriedOutputs.Add(output);
                return "NTFS";
            });

        if (succeeds) validate();
        else Assert.Throws<IOException>(validate);

        Assert.Equal([@"C:\", share], queriedRoots);
        if (succeeds)
            Assert.Equal([share + @"\scratch\temporary.iso", share + @"\outputs\foundry.iso"], queriedOutputs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capacity_RejectsOversizedIsoOnEitherFat32Output(bool preparedIsFat32)
    {
        const string prepared = @"C:\scratch\temporary.iso";
        const string requested = @"\\server\media\foundry.iso";

        Assert.Throws<IOException>(() => WinPeCustomImageIsoMastering.ValidateCapacity(@"C:\staging",
            prepared, requested, (long)uint.MaxValue + 1, _ => long.MaxValue,
            output => (output == prepared) == preparedIsFat32 ? "FAT32" : "NTFS"));
    }

    [Fact]
    public void Capacity_AcceptsLargeIsoOnNtfsShare()
    {
        WinPeCustomImageIsoMastering.ValidateCapacity(@"C:\staging", @"C:\scratch\temporary.iso",
            @"\\server\media\foundry.iso", (long)uint.MaxValue + 1, _ => long.MaxValue, _ => "NTFS");
    }

    [Theory]
    [InlineData(false, true, "bootx64.efi")]
    [InlineData(false, false, "bootaa64.efi")]
    [InlineData(true, true, "bootx64.efi")]
    [InlineData(true, false, "bootaa64.efi")]
    public void Mastering_OrdersBootFilesAndRetainsFirmwareSignatureChoice(bool bootEx, bool bios, string bootFileName)
    {
        string bins = Path.Combine(root, "bootbins");
        Directory.CreateDirectory(bins);
        File.WriteAllText(Path.Combine(bins, bootEx ? "efisys_EX.bin" : "efisys.bin"), "efi");
        File.WriteAllText(Path.Combine(bins, "bootmgfw.efi"), "legacy manager");
        File.WriteAllText(Path.Combine(bins, "bootmgfw_EX.efi"), "PCA2023 manager");
        if (bios) File.WriteAllText(Path.Combine(bins, "etfsboot.com"), "bios");
        string media = Path.Combine(root, "media");
        Directory.CreateDirectory(Path.Combine(media, "sources"));
        Directory.CreateDirectory(Path.Combine(media, "boot"));
        Directory.CreateDirectory(Path.Combine(media, "EFI", "Boot"));
        Directory.CreateDirectory(Path.Combine(media, "EFI", "Microsoft", "Boot"));
        Directory.CreateDirectory(Path.Combine(media, CustomImageMediaPaths.RelativeRoot));
        File.WriteAllText(Path.Combine(media, "sources", "boot.wim"), "boot");
        File.WriteAllText(Path.Combine(media, "boot", "BCD"), "bcd");
        string bootFile = Path.Combine(media, "EFI", "Boot", bootFileName);
        string microsoftBootFile = Path.Combine(media, "EFI", "Microsoft", "Boot", "bootmgfw.efi");
        File.WriteAllText(bootFile, "prior manager");
        File.WriteAllText(microsoftBootFile, "prior manager");
        File.WriteAllText(Path.Combine(media, CustomImageMediaPaths.RelativeRoot, "image.wim"), "custom image");

        string arguments = WinPeCustomImageIsoMastering.CreateArguments(root, Path.Combine(root, "output.iso"), bootEx);

        Assert.Contains("-u2 -udfver102", arguments);
        Assert.Contains(" -m ", arguments);
        Assert.Contains("-yo", arguments);
        Assert.Contains(bootEx ? "efisys_EX.bin" : "efisys.bin", arguments);
        Assert.Contains(bios ? "-bootdata:2#p0,e,b" : "-bootdata:1#pEF,e,b", arguments);
        string expectedManager = bootEx ? "PCA2023 manager" : "legacy manager";
        Assert.Equal(expectedManager, File.ReadAllText(bootFile));
        Assert.Equal(expectedManager, File.ReadAllText(microsoftBootFile));
        string order = File.ReadAllText(Path.Combine(root, "boot-order.txt"));
        Assert.Contains("boot\\BCD", order);
        Assert.EndsWith("sources\\boot.wim\r\n", order);
        Assert.DoesNotContain(CustomImageMediaPaths.RelativeRoot, order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_StagesExternalImagesSeparatelyAndPreservesPriorIsoOnCancellation(bool cancel)
    {
        string work = Path.Combine(root, "work");
        string media = Path.Combine(work, "media");
        Directory.CreateDirectory(Path.Combine(media, "sources"));
        Directory.CreateDirectory(Path.Combine(work, "bootbins"));
        File.WriteAllText(Path.Combine(media, "sources", "boot.wim"), "boot");
        File.WriteAllText(Path.Combine(work, "bootbins", "efisys.bin"), "efi");
        File.WriteAllText(Path.Combine(work, "bootbins", "bootmgfw.efi"), "legacy manager");
        string source = Path.Combine(root, "source.wim");
        File.WriteAllText(source, "custom image");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("custom image")));
        string relative = Path.Combine("Cache", "OperatingSystems", "Custom", hash.ToLowerInvariant(), "image.wim");
        using var package = new WinPeCustomImageMediaLease("build", Encoding.UTF8.GetBytes("{}"),
            [new(source, relative, 12, hash)], []);
        string configuration = WinPeCustomImageMediaService.BindConfiguration(package, """{"customImages":{"isEnabled":true}}""");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runner = new IsoRunner((staging, output) =>
        {
            Assert.Equal("custom image", File.ReadAllText(Path.Combine(staging, "media", relative)));
            Assert.True(File.Exists(Path.Combine(staging, "media", package.ManifestRelativePath)));
            Assert.False(Directory.Exists(Path.Combine(media, CustomImageMediaPaths.RelativeRoot)));
            File.WriteAllText(output, "new ISO");
            if (cancel) cancellation.Cancel();
        });
        string output = Path.Combine(root, "output.iso");
        File.WriteAllText(output, "prior ISO");
        var service = new WinPeIsoMediaService(runner);
        var options = new WinPeIsoMediaOptions
        {
            CustomImages = package,
            DeployConfigurationJson = configuration,
            OutputIsoPath = output,
            IsoTempDirectoryPath = Path.Combine(root, "scratch"),
            PreparedWorkspace = new()
            {
                Artifact = new() { WorkingDirectoryPath = work, MediaDirectoryPath = media },
                Tools = new() { MakeWinPeMediaPath = "MakeWinPEMedia.cmd", OscdimgPath = "oscdimg.exe" }
            }
        };

        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(options, cancellation.Token));
        else
        {
            WinPeResult result = await service.CreateAsync(options, cancellation.Token);
            Assert.True(result.IsSuccess, result.Error?.Details);
        }

        Assert.Equal(cancel ? "prior ISO" : "new ISO", File.ReadAllText(output));
        Assert.Empty(Directory.EnumerateFiles(root, "*.pending.iso"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "scratch"), "custom-iso-*"));
    }

    private sealed class IsoRunner(Action<string, string> run) : IWinPeProcessRunner
    {
        public Task<WinPeProcessExecution> RunAsync(string file, string arguments, string working, CancellationToken token, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            Assert.Contains("-u2 -udfver102", arguments);
            Match last = Regex.Match(arguments, "(?:\"([^\"]+)\"|(\\S+))$");
            string output = last.Groups[1].Success ? last.Groups[1].Value : last.Groups[2].Value;
            run(working, output);
            return Task.FromResult(new WinPeProcessExecution { ExitCode = 0 });
        }
        public Task<WinPeProcessExecution> RunCmdScriptAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string script, string arguments, string working, CancellationToken token) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
