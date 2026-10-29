// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Media;

namespace Foundry.Core.Tests.Media;

public sealed class PxeMissingContentTests
{
    [Fact]
    public void Evaluate_CountsIncludedImportedContentOfEnabledPages()
    {
        PxeMissingContent missing = PxeMissingContent.Evaluate(new FoundryConfigurationDocument
        {
            CustomImages = new CustomImagesSettings
            {
                IsEnabled = true,
                Images = [new CustomImageReference { Id = "a" }, new CustomImageReference { Id = "b", IsIncluded = false }]
            },
            CustomDrivers = new CustomDriverSetsSettings
            {
                IsEnabled = true,
                Sets = [new CustomDriverSetReference { Id = "a" }, new CustomDriverSetReference { Id = "b" }]
            },
            PreOobe = new PreOobeSettings
            {
                IsEnabled = true,
                Actions =
                [
                    CreateAction(withPackage: true),
                    CreateAction(withPackage: true) with { IsEnabled = false },
                    CreateAction(withPackage: false)
                ]
            }
        });

        Assert.Equal(new PxeMissingContent(1, 2, 1), missing);
        Assert.True(missing.HasAny);
    }

    [Fact]
    public void Evaluate_IgnoresImportedContentOfDisabledPages()
    {
        PxeMissingContent missing = PxeMissingContent.Evaluate(new FoundryConfigurationDocument
        {
            CustomImages = new CustomImagesSettings { IsEnabled = false, Images = [new CustomImageReference { Id = "a" }] },
            CustomDrivers = new CustomDriverSetsSettings { IsEnabled = false, Sets = [new CustomDriverSetReference { Id = "a" }] },
            PreOobe = new PreOobeSettings { IsEnabled = false, Actions = [CreateAction(withPackage: true)] }
        });

        Assert.Equal(new PxeMissingContent(0, 0, 0), missing);
        Assert.False(missing.HasAny);
    }

    [Fact]
    public void Evaluate_ReportsNothingMissingWhenTheNetworkCacheHasAShare()
    {
        FoundryConfigurationDocument configuration = CreateConfigurationWithImportedContent() with
        {
            NetworkCache = new NetworkCacheSettings { IsEnabled = true, Shares = [new NetworkCacheShare { Id = "s1", Path = @"\\fs01\cache" }] }
        };

        PxeMissingContent missing = PxeMissingContent.Evaluate(configuration);

        Assert.True(PxeMissingContent.IsServedByNetworkCache(configuration));
        Assert.Equal(new PxeMissingContent(0, 0, 0), missing);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Evaluate_CountsContentWhenTheNetworkCacheCannotServeIt(bool isEnabled, bool hasShare)
    {
        FoundryConfigurationDocument configuration = CreateConfigurationWithImportedContent() with
        {
            NetworkCache = new NetworkCacheSettings
            {
                IsEnabled = isEnabled,
                Shares = hasShare ? [new NetworkCacheShare { Id = "s1", Path = @"\\fs01\cache" }] : []
            }
        };

        PxeMissingContent missing = PxeMissingContent.Evaluate(configuration);

        Assert.False(PxeMissingContent.IsServedByNetworkCache(configuration));
        Assert.Equal(new PxeMissingContent(1, 1, 1), missing);
    }

    private static FoundryConfigurationDocument CreateConfigurationWithImportedContent() => new()
    {
        CustomImages = new CustomImagesSettings { IsEnabled = true, Images = [new CustomImageReference { Id = "a" }] },
        CustomDrivers = new CustomDriverSetsSettings { IsEnabled = true, Sets = [new CustomDriverSetReference { Id = "a" }] },
        PreOobe = new PreOobeSettings { IsEnabled = true, Actions = [CreateAction(withPackage: true)] }
    };

    private static PreOobeActionSettings CreateAction(bool withPackage) =>
        PreOobeActionSettings.Create(withPackage ? PreOobeActionKind.Application : PreOobeActionKind.Command, "Action") with
        {
            Package = withPackage ? new ContentPackageReference { ContentHash = "hash", DisplayName = "Package" } : null
        };
}
