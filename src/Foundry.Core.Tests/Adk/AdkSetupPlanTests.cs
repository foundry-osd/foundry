// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkSetupPlanTests
{
    private const string SupportedVersion = "10.1.26100.2454";

    [Fact]
    public void Create_WhenNothingIsInstalled_InstallsAdkAndWinPeWithoutUninstall()
    {
        AdkSetupPlan plan = AdkSetupPlan.Create(CreateStatus(false, false, null, AdkVersionRelation.Unknown));

        Assert.Equal(new AdkSetupPlan(AdkSetupAction.Install, false, true, true), plan);
    }

    [Fact]
    public void Create_WhenCompatibleAdkLacksWinPeAddon_InstallsOnlyWinPeAddon()
    {
        AdkSetupPlan plan = AdkSetupPlan.Create(CreateStatus(true, true, SupportedVersion, AdkVersionRelation.Supported));

        Assert.Equal(new AdkSetupPlan(AdkSetupAction.Install, false, false, true), plan);
    }

    [Fact]
    public void Create_WhenCompatibleAdkHasRegisteredButUnusableWinPeAddon_KeepsManualRepair()
    {
        AdkInstallationStatus status = CreateStatus(true, true, SupportedVersion, AdkVersionRelation.Supported) with
        {
            IsWinPeAddonRegistered = true
        };

        Assert.Equal(AdkSetupAction.None, AdkSetupPlan.Create(status).Action);
    }

    [Theory]
    [InlineData("10.1.28000.1", AdkVersionRelation.AboveSupported)]
    [InlineData("10.1.22621.1", AdkVersionRelation.BelowSupported)]
    public void Create_WhenOtherReleaseIsRegisteredWithoutDeploymentTools_UninstallsBeforeInstall(
        string installedVersion,
        AdkVersionRelation versionRelation)
    {
        AdkSetupPlan plan = AdkSetupPlan.Create(CreateStatus(false, false, installedVersion, versionRelation));

        Assert.Equal(new AdkSetupPlan(AdkSetupAction.Upgrade, true, true, true), plan);
    }

    [Fact]
    public void Create_WhenInstalledAdkHasNoRegisteredVersion_UpgradesWithFullInstall()
    {
        AdkSetupPlan plan = AdkSetupPlan.Create(CreateStatus(true, false, null, AdkVersionRelation.Unknown));

        Assert.Equal(new AdkSetupPlan(AdkSetupAction.Upgrade, true, true, true), plan);
    }

    private static AdkInstallationStatus CreateStatus(
        bool isInstalled,
        bool isCompatible,
        string? installedVersion,
        AdkVersionRelation versionRelation) =>
        new(isInstalled, isCompatible, false, installedVersion, versionRelation, null, "Windows ADK 24H2 / 10.1.26100.2454");
}
