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
    public void AutomaticIntegrationPreservesOriginalAndAppendsAfterUserCommands()
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
        using var result = service.Evaluate(source, "x64");
        Assert.Equal(snapshot, source);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(source)), result.SourceSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(result.DerivedContent.Span)), result.DerivedSha256);
        Assert.NotEqual(result.SourceSha256, result.DerivedSha256);
        Assert.True(result.IsModified);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        var derived = XDocument.Parse(Encoding.UTF8.GetString(result.DerivedContent.Span));
        XElement[] commands = derived.Descendants(ns + "RunSynchronousCommand").ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal("19", (string?)commands[0].Element(ns + "Order"));
        Assert.Equal("private-description", (string?)commands[0].Element(ns + "Description"));
        Assert.Equal("private-command.exe --password=private-password", (string?)commands[0].Element(ns + "Path"));
        XElement command = commands[1];
        Assert.Equal("20", (string?)command.Element(ns + "Order"));
        Assert.Equal(PreOobeUnattendIntegrationService.Command, (string?)command.Element(ns + "Path"));
        Assert.Equal("OnRequest", (string?)command.Element(ns + "WillReboot"));
        Assert.Contains("private-password", Encoding.UTF8.GetString(result.DerivedContent.Span));
    }

    [Fact]
    public void ExistingCanonicalHookIsIdempotentAndDisposeClearsOwnedBytes()
    {
        using var integrated = service.Evaluate(Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "arm64");
        byte[] source = integrated.DerivedContent.ToArray();
        var result = service.Evaluate(source, "arm64");
        Assert.Equal(source, result.DerivedContent.ToArray());
        Assert.Equal(result.SourceSha256, result.DerivedSha256);
        Assert.False(result.IsModified);
        ReadOnlyMemory<byte> owned = result.DerivedContent;
        result.Dispose();
        Assert.All(owned.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => result.DerivedContent);
    }

    [Theory]
    [InlineData("<!DOCTYPE unattend [<!ENTITY secret SYSTEM 'file:///private-secret'>]><unattend xmlns='urn:schemas-microsoft-com:unattend'>&secret;</unattend>")]
    [InlineData("<private-secret")]
    public void InvalidXmlProducesSanitizedValidation(string xml)
    {
        var exception = Assert.Throws<InvalidDataException>(() => service.Evaluate(Encoding.UTF8.GetBytes(xml), "x64"));
        Assert.DoesNotContain("private-secret", exception.Message);
    }

    [Fact]
    public void OversizedSourceIsRejectedBeforeParsing()
    {
        byte[] source = new byte[UnattendFileService.MaximumFileSizeBytes + 1];
        Assert.Throws<InvalidDataException>(() => service.Evaluate(source, "x64"));
    }

    [Theory]
    [InlineData("action", "remove")]
    [InlineData("Path", "another.exe")]
    [InlineData("WillReboot", "Always")]
    public void ConflictingOwnedHookIsRejectedWithoutChangingSource(string setting, string value)
    {
        using var initial = service.Evaluate(Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "x64");
        var source = XDocument.Parse(Encoding.UTF8.GetString(initial.DerivedContent.Span));
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        XElement command = source.Descendants(ns + "RunSynchronousCommand").Single();
        if (setting == "action") command.SetAttributeValue(wcm + "action", value);
        else command.SetElementValue(ns + setting, value);
        byte[] bytes = Encoding.UTF8.GetBytes(source.ToString());
        byte[] snapshot = bytes.ToArray();
        Assert.Throws<InvalidDataException>(() => service.Evaluate(bytes, "x64"));
        Assert.Equal(snapshot, bytes);
    }
}
