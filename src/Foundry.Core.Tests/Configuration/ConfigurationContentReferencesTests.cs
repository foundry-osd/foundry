// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationContentReferencesTests
{
    private static ContentPackageReference Package(string hash) => new() { ContentHash = hash };

    private static CustomDriverSetReference DriverSet(string id, string hash, bool included = true) =>
        new() { Id = id, Package = Package(hash), IsIncluded = included };

    [Fact]
    public void PostInstallationPackageHashes_ListsEveryActionPackage()
    {
        var configuration = new FoundryConfigurationDocument
        {
            PreOobe = new()
            {
                IsEnabled = false,
                Actions =
                [
                    new() { Package = Package("AA") },
                    new() { Package = Package("bb"), IsEnabled = false },
                    new() { Package = null },
                    new() { Package = Package("aa") },
                ]
            }
        };

        IReadOnlySet<string> hashes = ConfigurationContentReferences.PostInstallationPackageHashes(configuration);

        Assert.Equal(2, hashes.Count);
        Assert.Contains("AA", hashes);
        Assert.Contains("BB", hashes);
    }

    [Fact]
    public void DriverSetHashes_IncludesExcludedSets()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomDrivers = new() { IsEnabled = true, Sets = [DriverSet("1", "aa"), DriverSet("2", "bb", included: false)] }
        };

        IReadOnlySet<string> hashes = ConfigurationContentReferences.DriverSetHashes(configuration);

        Assert.Equal(["aa", "bb"], hashes.Order());
    }

    [Fact]
    public void DriverSetHashes_DisabledSectionStillListsSets()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomDrivers = new() { IsEnabled = false, Sets = [DriverSet("1", "aa")] }
        };

        Assert.Contains("AA", ConfigurationContentReferences.DriverSetHashes(configuration));
    }

    [Fact]
    public void DriverSetHashes_RemovedSetIsNotListed()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomDrivers = new() { Sets = [DriverSet("1", "aa"), DriverSet("2", "bb")] }
        };
        FoundryConfigurationDocument afterRemoval = configuration with
        {
            CustomDrivers = configuration.CustomDrivers with { Sets = configuration.CustomDrivers.Sets.Where(set => set.Id != "2").ToArray() }
        };

        IReadOnlySet<string> hashes = ConfigurationContentReferences.DriverSetHashes(afterRemoval);

        Assert.Equal(["aa"], hashes);
    }

    [Fact]
    public void CustomImageHashes_ListsEveryImage()
    {
        var configuration = new FoundryConfigurationDocument
        {
            CustomImages = new()
            {
                IsEnabled = false,
                Images =
                [
                    new() { Id = "1", ContentHash = "aa" },
                    new() { Id = "2", ContentHash = "bb", IsIncluded = false },
                ]
            }
        };

        IReadOnlySet<string> hashes = ConfigurationContentReferences.CustomImageHashes(configuration);

        Assert.Equal(["aa", "bb"], hashes.Order());
        Assert.Contains("BB", hashes);
    }

    [Fact]
    public void AuthoringFileHashes_ListsEveryFileReferenceIncludingAnswerFiles()
    {
        static AuthoringFileReference File(string hash) => new() { ContentHash = hash, FileName = "f", Length = 1 };
        var configuration = new FoundryConfigurationDocument
        {
            Network = new()
            {
                Dot1x = new() { IsEnabled = false, ProfileTemplate = File("aa"), Certificate = File("bb") },
                Wifi = new() { EnterpriseProfileTemplate = File("cc"), Certificate = File("bb") }
            },
            Autopilot = new() { HardwareHashUpload = new() { BootMediaCertificate = new() { Pfx = File("dd") } } },
            Unattend = new() { IsEnabled = false, Files = [new() { Id = "1", ContentHash = "ee", File = File("ee") }, new() { Id = "2", ContentHash = "ff" }] }
        };

        IReadOnlySet<string> hashes = ConfigurationContentReferences.AuthoringFileHashes(configuration);

        Assert.Equal(["aa", "bb", "cc", "dd", "ee"], hashes.Order());
        Assert.Contains("DD", hashes);
    }
}
