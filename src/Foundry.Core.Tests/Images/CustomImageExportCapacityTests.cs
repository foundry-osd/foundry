// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Images;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImageExportCapacityTests
{
    [Theory]
    [InlineData(null, "NTFS")]
    [InlineData(-1L, "NTFS")]
    [InlineData(2147483648L, "NTFS")]
    [InlineData(10737418240L, "FAT32")]
    [InlineData(10737418240L, "")]
    public void ExportRejectsUnavailableCapacityOrUnsupportedOutput(long? available, string format)
    {
        CustomImageIndex[] indexes = [new() { ExpandedSizeBytes = 3221225472 }, new() { ExpandedSizeBytes = 2147483648 }];
        Assert.Throws<IOException>(() => CustomImageExportCapacityPolicy.Validate(indexes, available, format));
    }

    [Fact]
    public void ExportReservesSpaceBeyondExpandedImageBytes()
    {
        CustomImageIndex[] indexes = [new() { ExpandedSizeBytes = 2147483648 }];
        Assert.Throws<IOException>(() => CustomImageExportCapacityPolicy.Validate(indexes, 2147483648, "NTFS"));
        CustomImageExportCapacityPolicy.Validate(indexes, 4294967296, "NTFS");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExportRejectsUnknownExpandedSize(long expandedSize)
    {
        Assert.Throws<InvalidDataException>(() => CustomImageExportCapacityPolicy.Validate(
            [new() { ExpandedSizeBytes = expandedSize }], long.MaxValue, "NTFS"));
    }
}
