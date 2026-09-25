// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageSelectionTests
{
    [Fact]
    public void Selection_PreservesUnknownMetadataAndExactIndexWithoutCatalogDefaults()
    {
        var index = new CustomImageIndex { Index = 9, Name = "Custom", Architecture = "x86", EditionId = "UnknownEdition", ExpandedSizeBytes = 1024 };
        var selection = new CustomImageSelection(new CustomImageAsset { Id = "manual", DisplayName = "Image", ImagePath = @"D:\Cache\OperatingSystems\Custom\image.wim", VolumeRoot = @"D:\" }, index);
        Assert.Equal(9, selection.Index.Index);
        Assert.Equal("x86", selection.Architecture);
        Assert.Equal("UnknownEdition", selection.Edition);
        Assert.Empty(selection.ReleaseId);
        Assert.Empty(selection.LicenseChannel);
        Assert.Empty(selection.WindowsRelease);
    }
}
