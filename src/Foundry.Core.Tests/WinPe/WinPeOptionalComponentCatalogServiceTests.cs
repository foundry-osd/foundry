// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeOptionalComponentCatalogServiceTests
{
    [Fact]
    public void GetAvailableComponents_ReturnsNeutralComponentsWithDefaultsFlagged()
    {
        using TempAdk adk = TempAdk.Create(WinPeArchitecture.X64);
        adk.AddNeutralComponent("WinPE-WMI");
        adk.AddNeutralComponent("WinPE-PowerShell");
        adk.AddNeutralComponent("WinPE-HTA");
        adk.AddFile("lp.cab"); // language pack, not a component
        adk.AddFile("notacab.txt");

        var service = new WinPeOptionalComponentCatalogService();

        WinPeResult<IReadOnlyList<WinPeOptionalComponent>> result =
            service.GetAvailableComponents(adk.KitsRootPath, WinPeArchitecture.X64);

        Assert.True(result.IsSuccess, result.Error?.Details);
        IReadOnlyList<WinPeOptionalComponent> components = result.Value!;

        Assert.Equal(["WinPE-HTA", "WinPE-PowerShell", "WinPE-WMI"], components.Select(component => component.Name));
        Assert.True(components.Single(component => component.Name == "WinPE-PowerShell").IsRecommendedDefault);
        Assert.True(components.Single(component => component.Name == "WinPE-WMI").IsRecommendedDefault);
        Assert.False(components.Single(component => component.Name == "WinPE-HTA").IsRecommendedDefault);
    }

    [Fact]
    public void GetAvailableComponents_WhenFolderMissing_ReturnsFailure()
    {
        using TempAdk adk = TempAdk.Create(WinPeArchitecture.Arm64, createOcFolder: false);

        var service = new WinPeOptionalComponentCatalogService();

        WinPeResult<IReadOnlyList<WinPeOptionalComponent>> result =
            service.GetAvailableComponents(adk.KitsRootPath, WinPeArchitecture.Arm64);

        Assert.False(result.IsSuccess);
        Assert.Contains("optional components folder", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempAdk : IDisposable
    {
        private TempAdk(string rootPath, string ocRootPath)
        {
            KitsRootPath = rootPath;
            OcRootPath = ocRootPath;
        }

        public string KitsRootPath { get; }
        public string OcRootPath { get; }

        public static TempAdk Create(WinPeArchitecture architecture, bool createOcFolder = true)
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-oc-{Guid.NewGuid():N}");
            string archFolder = architecture == WinPeArchitecture.Arm64 ? "arm64" : "amd64";
            string ocRoot = Path.Combine(root, "Assessment and Deployment Kit", "Windows Preinstallation Environment", archFolder, "WinPE_OCs");
            Directory.CreateDirectory(root);
            if (createOcFolder)
            {
                Directory.CreateDirectory(ocRoot);
            }

            return new TempAdk(root, ocRoot);
        }

        public void AddNeutralComponent(string name)
        {
            AddFile($"{name}.cab");
        }

        public void AddFile(string fileName)
        {
            File.WriteAllText(Path.Combine(OcRootPath, fileName), "cab");
        }

        public void Dispose()
        {
            Directory.Delete(KitsRootPath, recursive: true);
        }
    }
}
