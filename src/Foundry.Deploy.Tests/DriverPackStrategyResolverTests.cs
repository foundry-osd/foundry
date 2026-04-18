using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Tests;

public sealed class DriverPackStrategyResolverTests
{
    [Fact]
    public void Resolve_WhenSelectionUsesMicrosoftUpdateCatalog_ReturnsOfflineInfPlan()
    {
        var resolver = new DriverPackStrategyResolver();

        DriverPackExecutionPlan plan = resolver.Resolve(
            DriverPackSelectionKind.MicrosoftUpdateCatalog,
            driverPack: null,
            downloadedPath: @"C:\Temp\surface.cab");

        Assert.Equal(DriverPackInstallMode.OfflineInf, plan.InstallMode);
        Assert.Equal(DriverPackExtractionMethod.MicrosoftUpdateCatalogExpand, plan.ExtractionMethod);
        Assert.Equal(DeferredDriverPackageCommandKind.None, plan.DeferredCommandKind);
        Assert.Equal(".cab", plan.EffectiveFileExtension);
        Assert.True(plan.RequiresInfPayload);
    }

    [Fact]
    public void Resolve_WhenManufacturerIsLenovoExecutable_ReturnsDeferredExecutionPlan()
    {
        var resolver = new DriverPackStrategyResolver();
        var driverPack = new DriverPackCatalogItem
        {
            Manufacturer = "Lenovo",
            FileName = "pack.exe"
        };

        DriverPackExecutionPlan plan = resolver.Resolve(
            DriverPackSelectionKind.OemCatalog,
            driverPack,
            downloadedPath: @"C:\Temp\pack.exe");

        Assert.Equal(DriverPackInstallMode.DeferredSetupComplete, plan.InstallMode);
        Assert.Equal(DriverPackExtractionMethod.None, plan.ExtractionMethod);
        Assert.Equal(DeferredDriverPackageCommandKind.LenovoExecutable, plan.DeferredCommandKind);
        Assert.False(plan.RequiresInfPayload);
    }
}
