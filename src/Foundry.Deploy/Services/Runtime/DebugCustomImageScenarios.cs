// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Runtime;

/// <summary>Supplies immutable in-memory inputs to the normal selection flow without accessing images or DISM.</summary>
public sealed record DebugCustomImageSnapshot(
    DebugCustomImageScenario Scenario,
    DeployCustomImagesSettings Settings,
    IReadOnlyList<CustomImageAsset> Images,
    IReadOnlyList<CustomImageIndex> Indexes,
    string CatalogErrorKey = "",
    string InspectionErrorKey = "");

/// <summary>Creates synthetic selection states; callers must enforce debug safety and restore real configuration themselves.</summary>
public static class DebugCustomImageScenarios
{
    private const string ManagedId = "debug-managed-image";
    private const long ImageLength = 5L * 1024 * 1024 * 1024;

    public static DebugCustomImageSnapshot Create(DebugCustomImageScenario scenario)
    {
        if (scenario == DebugCustomImageScenario.Configured || !Enum.IsDefined(scenario))
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Configured images must come from the active deployment configuration.");

        var managed = new CustomImageAsset
        {
            Id = ManagedId,
            DisplayName = "Managed Windows image",
            ImagePath = @"X:\Foundry\Debug\Images\managed\image.wim",
            VolumeRoot = @"X:\",
            ExpectedLength = ImageLength,
            ExpectedHash = new string('a', 64)
        };
        var manual = new CustomImageAsset
        {
            Id = "debug-manual-image",
            DisplayName = "Manual Windows image",
            ImagePath = @"X:\Foundry\Debug\Images\manual.wim",
            VolumeRoot = @"X:\"
        };
        IReadOnlyList<CustomImageAsset> images = scenario switch
        {
            DebugCustomImageScenario.Disabled or DebugCustomImageScenario.Empty => Array.AsReadOnly(Array.Empty<CustomImageAsset>()),
            DebugCustomImageScenario.CatalogDefault or DebugCustomImageScenario.MultipleIndexes => Array.AsReadOnly(new[] { managed, manual }),
            DebugCustomImageScenario.AmbiguousDefault => Array.AsReadOnly(new[]
            {
                managed,
                managed with { DisplayName = "Managed Windows image (second copy)", ImagePath = @"X:\Foundry\Debug\Images\duplicate\image.wim" }
            }),
            _ => Array.AsReadOnly(new[] { managed })
        };
        IReadOnlyList<CustomImageIndex> indexes = scenario switch
        {
            DebugCustomImageScenario.Disabled or DebugCustomImageScenario.Empty or DebugCustomImageScenario.InvalidImage => Array.AsReadOnly(Array.Empty<CustomImageIndex>()),
            DebugCustomImageScenario.SingleIndex => Array.AsReadOnly(new[] { CreateIndex(1, "Windows Professional", "Professional") }),
            _ => Array.AsReadOnly(new[]
            {
                CreateIndex(1, "Windows Professional", "Professional"),
                CreateIndex(4, "Windows Enterprise", "Enterprise"),
                CreateIndex(9, "Windows Enterprise - customized", "Enterprise")
            })
        };
        var settings = new DeployCustomImagesSettings
        {
            IsEnabled = scenario != DebugCustomImageScenario.Disabled,
            DefaultSource = scenario is DebugCustomImageScenario.Disabled or DebugCustomImageScenario.CatalogDefault
                ? CustomImageSource.Catalog : CustomImageSource.Custom,
            DefaultImageId = scenario switch
            {
                DebugCustomImageScenario.Disabled or DebugCustomImageScenario.Empty or DebugCustomImageScenario.MultipleIndexes => null,
                DebugCustomImageScenario.MissingImage => "debug-missing-image",
                _ => ManagedId
            },
            DefaultImageIndex = scenario switch
            {
                DebugCustomImageScenario.PreferredIndex => 9,
                DebugCustomImageScenario.MissingIndex => 99,
                _ => null
            },
            ManifestId = "debug-preview",
            ManifestHash = new string('b', 64)
        };
        return new(scenario, settings, images, indexes,
            scenario == DebugCustomImageScenario.Empty ? "CustomImages.NoImages" : string.Empty,
            scenario == DebugCustomImageScenario.InvalidImage ? "CustomImages.InvalidSource" : string.Empty);
    }

    private static CustomImageIndex CreateIndex(int index, string name, string edition) => new()
    {
        Index = index,
        Name = name,
        Architecture = "x64",
        EditionId = edition,
        ProductType = "WinNT",
        Build = 26100,
        Version = "10.0.26100.4652",
        DefaultLanguage = "en-US",
        Languages = Array.AsReadOnly(new[] { "en-US", "fr-FR" }),
        ExpandedSizeBytes = 20L * 1024 * 1024 * 1024
    };
}
