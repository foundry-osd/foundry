// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeOa3ToolResolverTests
{
    [Theory]
    [InlineData(WinPeArchitecture.X64, "amd64")]
    [InlineData(WinPeArchitecture.Arm64, "arm64")]
    public void Resolve_ChoosesArchitectureSpecificAdkToolPath(WinPeArchitecture architecture, string adkArchitecture)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-oa3-{Guid.NewGuid():N}");
        string oa3ToolPath = Path.Combine(
            root,
            "Assessment and Deployment Kit",
            "Deployment Tools",
            adkArchitecture,
            "Licensing",
            "OA30",
            "oa3tool.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(oa3ToolPath)!);
        File.WriteAllText(oa3ToolPath, "oa3");

        try
        {
            WinPeResult<string> result = WinPeOa3ToolResolver.Resolve(root, architecture);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal(oa3ToolPath, result.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenOa3ToolIsMissing_ReturnsToolNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-oa3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            WinPeResult<string> result = WinPeOa3ToolResolver.Resolve(root, WinPeArchitecture.X64);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
            Assert.Contains("OA3Tool", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
