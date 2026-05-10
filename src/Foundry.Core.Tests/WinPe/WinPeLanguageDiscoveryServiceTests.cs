using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeLanguageDiscoveryServiceTests
{
    [Fact]
    public void GetAvailableLanguages_ReturnsNormalizedSortedLanguageDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-language-discovery-{Guid.NewGuid():N}");
        CreateOptionalComponentLanguage(root, "amd64", "fr-FR");
        CreateOptionalComponentLanguage(root, "amd64", "en-US");
        CreateOptionalComponentLanguage(root, "amd64", "EN-us");

        var service = new WinPeLanguageDiscoveryService();

        try
        {
            WinPeResult<IReadOnlyList<string>> result = service.GetAvailableLanguages(
                new WinPeLanguageDiscoveryOptions
                {
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root
                    }
                });

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal(["en-us", "fr-fr"], result.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetAvailableLanguages_WhenOptionalComponentsRootIsMissing_ReturnsToolNotFound()
    {
        var service = new WinPeLanguageDiscoveryService();

        WinPeResult<IReadOnlyList<string>> result = service.GetAvailableLanguages(
            new WinPeLanguageDiscoveryOptions
            {
                Architecture = WinPeArchitecture.X64,
                Tools = new WinPeToolPaths
                {
                    KitsRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
                }
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
    }

    private static void CreateOptionalComponentLanguage(string kitsRootPath, string architecture, string language)
    {
        Directory.CreateDirectory(Path.Combine(
            kitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            architecture,
            "WinPE_OCs",
            language));
    }
}
