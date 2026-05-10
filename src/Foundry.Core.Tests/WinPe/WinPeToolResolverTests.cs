using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeToolResolverTests
{
    [Fact]
    public void ResolveTools_WhenKitsRootCannotBeFound_ReturnsToolNotFound()
    {
        var resolver = new WinPeToolResolver(() => null);

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools();

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
    }

    [Fact]
    public void ResolveTools_WhenWinPeToolsAreMissing_ReturnsToolNotFound()
    {
        using var tempDirectory = new TemporaryDirectory();
        var resolver = new WinPeToolResolver(() => null);

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools(tempDirectory.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
    }

    [Fact]
    public void ResolveTools_WhenToolsExist_ReturnsResolvedPaths()
    {
        using var tempDirectory = new TemporaryDirectory();
        string winPeRoot = Path.Combine(tempDirectory.Path, "Assessment and Deployment Kit", "Windows Preinstallation Environment");
        Directory.CreateDirectory(winPeRoot);
        string copypePath = Path.Combine(winPeRoot, "copype.cmd");
        string makeWinPeMediaPath = Path.Combine(winPeRoot, "MakeWinPEMedia.cmd");
        File.WriteAllText(copypePath, string.Empty);
        File.WriteAllText(makeWinPeMediaPath, string.Empty);

        var resolver = new WinPeToolResolver(() => null);

        WinPeResult<WinPeToolPaths> result = resolver.ResolveTools(tempDirectory.Path);

        Assert.True(result.IsSuccess);
        Assert.Equal(tempDirectory.Path, result.Value?.KitsRootPath);
        Assert.Equal(copypePath, result.Value?.CopypePath);
        Assert.Equal(makeWinPeMediaPath, result.Value?.MakeWinPeMediaPath);
        Assert.EndsWith("dism.exe", result.Value?.DismPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("cmd.exe", result.Value?.CmdPath, StringComparison.OrdinalIgnoreCase);
    }
}
