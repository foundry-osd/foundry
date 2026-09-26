// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class PreOobeUnattendIntegrationServiceTests
{
    private readonly PreOobeUnattendIntegrationService service = new();

    [Fact]
    public void PreviewUsesActualInsertionOrderWithoutExposingForeignSourceValues()
    {
        byte[] source = Encoding.UTF8.GetBytes("""
            <unattend xmlns="urn:schemas-microsoft-com:unattend">
              <!-- private-source-comment -->
              <settings pass="specialize"><component name="Microsoft-Windows-Deployment" processorArchitecture="amd64">
                <RunSynchronous><RunSynchronousCommand><Order>19</Order><Description>private-description</Description>
                  <Path>private-command.exe --password=private-password</Path></RunSynchronousCommand></RunSynchronous>
              </component></settings>
            </unattend>
            """);
        byte[] snapshot = source.ToArray();
        using var result = service.Evaluate(source, "x64", integrate: true);
        Assert.Equal(snapshot, source);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(source)), result.SourceSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(result.DerivedContent.Span)), result.DerivedSha256);
        Assert.NotEqual(result.SourceSha256, result.DerivedSha256);
        Assert.True(result.IsModified);
        Assert.DoesNotContain("private-", result.SanitizedPreview);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        var preview = XElement.Parse(result.SanitizedPreview);
        Assert.Equal("specialize", (string?)preview.Attribute("pass"));
        XElement command = Assert.Single(preview.Descendants(ns + "RunSynchronousCommand"));
        Assert.Equal("20", (string?)command.Element(ns + "Order"));
        Assert.Equal(PreOobeUnattendIntegrationService.Command, (string?)command.Element(ns + "Path"));
        Assert.Contains("private-password", Encoding.UTF8.GetString(result.DerivedContent.Span));
    }

    [Fact]
    public void ExactCopyPreservesHashAndDisposeClearsOwnedBytes()
    {
        using var integrated = service.Evaluate(Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "arm64", true);
        byte[] source = integrated.DerivedContent.ToArray();
        var result = service.Evaluate(source, "arm64", false);
        Assert.Equal(source, result.DerivedContent.ToArray());
        Assert.Equal(result.SourceSha256, result.DerivedSha256);
        Assert.False(result.IsModified);
        ReadOnlyMemory<byte> owned = result.DerivedContent;
        result.Dispose();
        Assert.All(owned.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => result.DerivedContent);
        Assert.Contains("arm64", result.SanitizedPreview);
    }

    [Theory]
    [InlineData("<!DOCTYPE unattend [<!ENTITY secret SYSTEM 'file:///private-secret'>]><unattend xmlns='urn:schemas-microsoft-com:unattend'>&secret;</unattend>")]
    [InlineData("<private-secret")]
    public void InvalidXmlProducesSanitizedValidation(string xml)
    {
        var exception = Assert.Throws<InvalidDataException>(() => service.Evaluate(Encoding.UTF8.GetBytes(xml), "x64", true));
        Assert.DoesNotContain("private-secret", exception.Message);
    }

    [Fact]
    public void OversizedSourceIsRejectedBeforeParsing()
    {
        byte[] source = new byte[UnattendFileService.MaximumFileSizeBytes + 1];
        Assert.Throws<InvalidDataException>(() => service.Evaluate(source, "x64", true));
    }

    [Fact]
    public void RemovalHookIsRejectedInExactCopyAndNormalizedByExplicitIntegration()
    {
        using var initial = service.Evaluate(Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "x64", true);
        var source = XDocument.Parse(Encoding.UTF8.GetString(initial.DerivedContent.Span));
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        source.Descendants(ns + "RunSynchronousCommand").Single().SetAttributeValue(wcm + "action", "remove");
        byte[] bytes = Encoding.UTF8.GetBytes(source.ToString());
        Assert.Throws<InvalidDataException>(() => service.Evaluate(bytes, "x64", false));
        using var integrated = service.Evaluate(bytes, "x64", true);
        var derived = XDocument.Parse(Encoding.UTF8.GetString(integrated.DerivedContent.Span));
        Assert.Equal("add", (string?)derived.Descendants(ns + "RunSynchronousCommand").Single().Attribute(wcm + "action"));
        Assert.Equal("add", (string?)XElement.Parse(integrated.SanitizedPreview).Descendants(ns + "RunSynchronousCommand").Single().Attribute(wcm + "action"));
    }
}
