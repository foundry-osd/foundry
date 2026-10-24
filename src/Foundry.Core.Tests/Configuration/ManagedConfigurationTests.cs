// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class ManagedConfigurationTests
{
    private static readonly FoundryConfigurationDocument Before = new()
    {
        General = new GeneralSettings
        {
            IsoOutputPath = "C:\\Media\\foundry.iso",
            CustomDriverDirectoryPath = "C:\\WinPeDrivers",
            WinPeLanguage = "en-US"
        },
        CustomDrivers = new CustomDriverSetsSettings
        {
            IsEnabled = true,
            Sets = [new CustomDriverSetReference { Id = "set-1", DisplayName = "Laptops", Package = new ContentPackageReference { ContentHash = new string('a', 64) } }],
            DefaultSetId = "set-1"
        }
    };

    [Fact]
    public void PerPcChange_IsAllowed()
    {
        FoundryConfigurationDocument after = Before with
        {
            General = Before.General with { IsoOutputPath = "D:\\Other\\foundry.iso", CustomDriverDirectoryPath = "D:\\OtherDrivers" },
            Telemetry = new TelemetrySettings { IsEnabled = true, InstallId = "install-2" }
        };

        Assert.True(ManagedConfigurationGuard.IsAllowed(Before, after));
        Assert.True(ManagedConfigurationGuard.IsAllowed(Before, Before with { }));
    }

    [Fact]
    public void SharedSectionChange_IsRejected()
    {
        FoundryConfigurationDocument after = Before with { General = Before.General with { WinPeLanguage = "fr-FR" } };

        Assert.False(ManagedConfigurationGuard.IsAllowed(Before, after));
        Assert.False(ManagedConfigurationGuard.IsAllowed(Before, Before with { Network = Before.Network with { WifiProvisioned = true } }));
    }

    [Fact]
    public void CustomDriversChange_IsRejected()
    {
        FoundryConfigurationDocument excluded = Before with
        {
            CustomDrivers = Before.CustomDrivers with { Sets = [Before.CustomDrivers.Sets[0] with { IsIncluded = false }] }
        };
        FoundryConfigurationDocument withoutDefault = Before with { CustomDrivers = Before.CustomDrivers with { DefaultSetId = null } };

        Assert.False(ManagedConfigurationGuard.IsAllowed(Before, excluded));
        Assert.False(ManagedConfigurationGuard.IsAllowed(Before, withoutDefault));
    }
}
