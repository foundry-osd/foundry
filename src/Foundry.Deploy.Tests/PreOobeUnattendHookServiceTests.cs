// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment.Unattend;
using Xunit;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeUnattendHookServiceTests
{
    private const string Namespace = "urn:schemas-microsoft-com:unattend";
    private readonly PreOobeUnattendHookService _service = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationWritesProtectedHashAuditWithoutSourceValues(bool integrate)
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string answer = Path.Combine(root, "Windows", "Panther", "unattend.xml");
        string audit = Path.Combine(root, "Windows", "Temp", "Foundry", "State", "PreOobe", "unattend-integration.json");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        try
        {
            byte[] source = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\"><!-- private-secret --></unattend>");
            if (!integrate) source = _service.Prepare(source, "x64", true);
            File.WriteAllBytes(answer, source);
            bool protectedBeforeAudit = false;
            var service = new PreOobeUnattendHookService(path =>
            { protectedBeforeAudit = !File.Exists(audit); Directory.CreateDirectory(path); });
            service.Publish(root, "x64", integrate);
            Assert.True(protectedBeforeAudit);
            string auditText = File.ReadAllText(audit);
            Assert.DoesNotContain("private-secret", auditText);
            Assert.DoesNotContain("unattend xmlns", auditText);
            using JsonDocument document = JsonDocument.Parse(auditText);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(source)), document.RootElement.GetProperty("sourceSha256").GetString());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(answer))), document.RootElement.GetProperty("derivedSha256").GetString());
            if (!integrate) Assert.Equal(source, File.ReadAllBytes(answer));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FailedAnswerPublicationRemovesOnlyItsNewAudit()
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string answer = Path.Combine(root, "Windows", "Panther", "unattend.xml");
        string audit = Path.Combine(root, "Windows", "Temp", "Foundry", "State", "PreOobe", "unattend-integration.json");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        try
        {
            byte[] source = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\"/>");
            File.WriteAllBytes(answer, source);
            var service = new PreOobeUnattendHookService(path => Directory.CreateDirectory(path));
            using (var held = new FileStream(answer, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => service.Publish(root, "x64", true));
            Assert.False(File.Exists(audit));
            Assert.Equal(source, File.ReadAllBytes(answer));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void IntegrationAppendsPreservesForeignCommandsAndIsIdempotent()
    {
        byte[] input = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\"><settings pass=\"specialize\"><component name=\"Microsoft-Windows-Deployment\" processorArchitecture=\"amd64\"><RunSynchronous><RunSynchronousCommand><Order>3</Order><Path>foreign.exe</Path></RunSynchronousCommand></RunSynchronous></component></settings></unattend>");
        byte[] output = _service.Prepare(input, "x64", true);
        Assert.Equal(output, _service.Prepare(output, "x64", true));
        XNamespace ns = Namespace;
        XElement[] commands = XDocument.Parse(Encoding.UTF8.GetString(output)).Descendants(ns + "RunSynchronousCommand").ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal("foreign.exe", commands[0].Element(ns + "Path")!.Value);
        Assert.Equal("4", commands[1].Element(ns + "Order")!.Value);
        Assert.Equal("OnRequest", commands[1].Element(ns + "WillReboot")!.Value);
        Assert.Equal(output, _service.Prepare(output, "x64", false));
    }

    [Fact]
    public void ExactCopyRequiresCanonicalHookAndPreservesBytes()
    {
        byte[] original = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\" />");
        Assert.Throws<InvalidDataException>(() => _service.Prepare(original, "x64", false));
        byte[] integrated = _service.Prepare(original, "x64", true);
        Assert.Equal(integrated, _service.Prepare(integrated, "x64", false));
        Assert.Equal($"<unattend xmlns=\"{Namespace}\" />", Encoding.UTF8.GetString(original));
    }

    [Theory]
    [InlineData("500")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("bad")]
    public void InvalidOrExhaustedOrdersAreRejected(string order)
    {
        byte[] input = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\"><settings pass=\"specialize\"><component name=\"Microsoft-Windows-Deployment\" processorArchitecture=\"amd64\"><RunSynchronous><RunSynchronousCommand><Order>{order}</Order><Path>foreign.exe</Path></RunSynchronousCommand></RunSynchronous></component></settings></unattend>");
        Assert.Throws<InvalidDataException>(() => _service.Prepare(input, "x64", true));
    }

    [Fact]
    public void ArchitectureConflictsAndDuplicateHooksAreRejected()
    {
        byte[] input = Encoding.UTF8.GetBytes($"<unattend xmlns=\"{Namespace}\" />");
        byte[] integrated = _service.Prepare(input, "arm64", true);
        Assert.Throws<InvalidDataException>(() => _service.Prepare(integrated, "x64", true));
        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(integrated));
        XNamespace ns = Namespace;
        XElement command = document.Descendants(ns + "RunSynchronousCommand").Single();
        command.Parent!.Add(new XElement(command));
        Assert.Throws<InvalidDataException>(() => _service.Prepare(Encoding.UTF8.GetBytes(document.ToString()), "arm64", true));
    }
}
