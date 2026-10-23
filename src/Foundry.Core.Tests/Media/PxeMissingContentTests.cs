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

    private static PreOobeActionSettings CreateAction(bool withPackage) =>
        PreOobeActionSettings.Create(withPackage ? PreOobeActionKind.Application : PreOobeActionKind.Command, "Action") with
        {
            Package = withPackage ? new ContentPackageReference { ContentHash = "hash", DisplayName = "Package" } : null
        };
}
