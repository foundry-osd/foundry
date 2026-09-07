// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class WindowsImageInfoParserTests
{
    internal const string Detail = """
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.2454

        Details for image : C:\fixture\install.esd

        Index : 4
        Name : Windows image
        Size : 15,098,360,792 bytes
        Architecture : x64
        Version : 10.0.26100
        ServicePack Build : 1000
        ServicePack Level : 0
        Edition : Professional
        Languages :
            en-US (Default)
            fr-FR
        The operation completed successfully.
        """;

    [Fact]
    public void Parse_ReadsTypedMetadataFromSyntheticEnglishDetail()
    {
        WindowsImageInfo result = WindowsImageInfoParser.Parse(Detail, 4);
        Assert.Equal(new WindowsImageInfo(4, "Professional", "x64", new Version(10, 0, 26100, 1000), "en-US", 15098360792), result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_SeparatesToolVersionFromImageVersion(string newline)
    {
        string output = Detail.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
        Assert.Equal(new Version(10, 0, 26100, 1000), WindowsImageInfoParser.Parse(output, 4).Version);
    }

    [Fact]
    public void Parse_DoesNotUseToolVersionWhenImageVersionIsMissing()
    {
        string output = Detail.Replace("Version : 10.0.26100", "", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => WindowsImageInfoParser.Parse(output, 4));
    }

    [Theory]
    [InlineData("Architecture : x64", "Architecture : AMD64")]
    [InlineData("Edition : Professional", "Edition ID : Professional")]
    [InlineData("Version : 10.0.26100", "Version : 10.0.26100.1000")]
    [InlineData("Languages :\n    en-US (Default)", "Languages : en-US (Default)")]
    public void Parse_AcceptsEquivalentSupportedFieldValues(string original, string replacement)
    {
        WindowsImageInfo result = WindowsImageInfoParser.Parse(Detail.Replace("\r\n", "\n", StringComparison.Ordinal).Replace(original, replacement, StringComparison.Ordinal), 4);
        Assert.Equal("x64", result.Architecture);
        Assert.Equal("Professional", result.EditionId);
        Assert.Equal("en-US", result.DefaultLanguage);
        Assert.Equal(1000, result.Version.Revision);
    }

    [Theory]
    [InlineData("Index : 4", "Index : 5")]
    [InlineData("Index : 4", "Index : 0")]
    [InlineData("Index : 4", "Index : 4\nIndex : 4")]
    [InlineData("Index : 4", "Index : 3\nIndex : 4")]
    [InlineData("Version : 10.0.26100", "Version : 10.0.26100\nVersion : 10.0.26100")]
    [InlineData("Architecture : x64", "Architecture : <undefined>")]
    [InlineData("Architecture : x64", "Architecture : x64\nArchitecture : x86")]
    [InlineData("Edition : Professional", "Edition : Professional\nEdition ID : Core")]
    [InlineData("Edition : Professional", "Edition : Professional\nEdition : Professional")]
    [InlineData("Size : 15,098,360,792 bytes", "Size : 9223372036854775808 bytes")]
    [InlineData("Size : 15,098,360,792 bytes", "Size : 1,23 bytes")]
    [InlineData("Size : 15,098,360,792 bytes", "Size : 0 bytes")]
    [InlineData("ServicePack Build : 1000", "ServicePack Build : -1")]
    [InlineData("Version : 10.0.26100", "Version : 10.0.26100.999")]
    [InlineData("en-US (Default)", "en-US")]
    [InlineData("en-US (Default)", "en-US (Default)\n    fr-FR (Default)")]
    [InlineData("Languages :", "Languages :\nLanguages :")]
    public void Parse_RejectsInvalidOrAmbiguousMetadata(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => WindowsImageInfoParser.Parse(Detail.Replace(original, replacement, StringComparison.Ordinal), 4));
    }

    [Theory]
    [InlineData("Index")]
    [InlineData("Size")]
    [InlineData("Architecture")]
    [InlineData("Version")]
    [InlineData("ServicePack Build")]
    [InlineData("Edition")]
    [InlineData("Languages")]
    public void Parse_RejectsMissingRequiredField(string field)
    {
        string output = string.Join('\n', Detail.Split('\n').Where(line => !line.TrimStart().StartsWith(field + " :", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => WindowsImageInfoParser.Parse(output, 4));
    }
}
