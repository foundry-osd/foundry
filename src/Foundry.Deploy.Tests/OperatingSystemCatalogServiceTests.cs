// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using System.IO;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemCatalogServiceTests
{
    [Fact]
    public void ParseCatalog_JoinsMediaDateFromSchemaVersionFourSource()
    {
        const string xml = """
            <OperatingSystemCatalog schemaVersion="4">
              <Sources>
                <Source id="Win11_25H2_26200.8873_20260710" build="26200.8873" buildMajor="26200" buildUbr="8873" mediaDate="2026-07-10" />
              </Sources>
              <Items>
                <Item>
                  <sourceId>Win11_25H2_26200.8873_20260710</sourceId>
                  <windowsRelease>11</windowsRelease>
                  <releaseId>25H2</releaseId>
                  <build>incorrect</build>
                  <buildMajor>1</buildMajor>
                  <buildUbr>2</buildUbr>
                  <architecture>x64</architecture>
                  <languageCode>en-US</languageCode>
                  <edition>Pro</edition>
                  <licenseChannel>RET</licenseChannel>
                  <fileName>windows.esd</fileName>
                  <url>https://example.test/windows.esd</url>
                </Item>
              </Items>
            </OperatingSystemCatalog>
            """;

        OperatingSystemCatalogItem item = Assert.Single(OperatingSystemCatalogService.ParseCatalog(xml));

        Assert.Equal(new DateOnly(2026, 7, 10), item.MediaDate);
        Assert.Equal("26200.8873", item.Build);
        Assert.Equal(26200, item.BuildMajor);
        Assert.Equal(8873, item.BuildUbr);
    }

    [Theory]
    [InlineData("""<OperatingSystemCatalog schemaVersion="3"><Sources /><Items /></OperatingSystemCatalog>""")]
    [InlineData("""
        <OperatingSystemCatalog schemaVersion="4">
          <Sources />
          <Items>
            <Item><sourceId>missing</sourceId><url>https://example.test/windows.esd</url></Item>
          </Items>
        </OperatingSystemCatalog>
        """)]
    [InlineData("""
        <OperatingSystemCatalog schemaVersion="4">
          <Sources>
            <Source id="invalid" build="26200.8873" buildMajor="26200" buildUbr="8873" mediaDate="2026-13-40" />
          </Sources>
          <Items />
        </OperatingSystemCatalog>
        """)]
    public void ParseCatalog_WhenContractIsInvalid_Throws(string xml)
    {
        Assert.Throws<InvalidDataException>(() => OperatingSystemCatalogService.ParseCatalog(xml));
    }
}
