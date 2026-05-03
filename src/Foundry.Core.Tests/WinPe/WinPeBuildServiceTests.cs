using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBuildServiceTests
{
    [Fact]
    public async Task BuildAsync_WhenOptionsAreNull_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenOutputDirectoryIsMissing_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions());

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenArchitectureIsInvalid_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions
        {
            OutputDirectoryPath = "C:\\Temp",
            Architecture = (WinPeArchitecture)999
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }
}
