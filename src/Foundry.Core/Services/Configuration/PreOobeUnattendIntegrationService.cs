// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Foundry.Core.Services.Configuration;

/// <summary>Adds the Foundry-owned specialize command to a derived answer file while preserving the source.</summary>
public sealed class PreOobeUnattendIntegrationService
{
    public const string Description = "Foundry PostInstall";
    public const string Command = "%SystemRoot%\\System32\\cmd.exe /d /s /c \"\"%SystemRoot%\\Temp\\Foundry\\Runtime\\PreOobe\\Launch.cmd\"\"";
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    /// <summary>Preserves the source and automatically derives the deployment copy with the required launch hook.</summary>
    public PreOobeUnattendIntegrationResult Evaluate(ReadOnlySpan<byte> source, string architecture)
    {
        if (source.Length is 0 or > UnattendFileService.MaximumFileSizeBytes)
            throw new InvalidDataException("The answer file exceeds the supported size limit or is empty.");
        byte[] inputBytes = source.ToArray();
        try
        {
            using var input = new MemoryStream(inputBytes, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = UnattendFileService.MaximumFileSizeBytes,
                MaxCharactersFromEntities = 0
            });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            ValidateOrUpdate(document, architecture);
            byte[] derived = Serialize(document);
            if (derived.Length > UnattendFileService.MaximumFileSizeBytes)
            {
                CryptographicOperations.ZeroMemory(derived);
                throw new InvalidDataException("The derived answer file exceeds the supported size limit.");
            }
            return new(derived, Convert.ToHexStringLower(SHA256.HashData(source)),
                Convert.ToHexStringLower(SHA256.HashData(derived)), !source.SequenceEqual(derived));
        }
        catch (XmlException)
        {
            throw new InvalidDataException("The answer file is not supported XML or contains prohibited document declarations.");
        }
        finally { CryptographicOperations.ZeroMemory(inputBytes); }
    }

    private static byte[] Serialize(XDocument document)
    {
        using var output = new MemoryStream();
        try
        {
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
            return output.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(output.GetBuffer().AsSpan(0, checked((int)output.Length))); }
    }

    private static void ValidateOrUpdate(XDocument document, string architecture)
    {
        if (document.Root?.Name != Ns + "unattend") throw new InvalidDataException("The answer file must have an unattend root.");
        string target = architecture.ToLowerInvariant() switch { "x64" or "amd64" => "amd64", "arm64" => "arm64", _ => throw new InvalidDataException("Post-installation requires an x64 or ARM64 Windows image.") };
        XElement[] candidates = document.Descendants(Ns + "RunSynchronousCommand")
            .Where(command => (string?)command.Element(Ns + "Description") == Description ||
                ((string?)command.Element(Ns + "Path"))?.Contains("\\Runtime\\PreOobe\\Launch.cmd", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (candidates.Length > 1) throw new InvalidDataException("The answer file contains duplicate Foundry post-installation commands.");
        XElement[] settings = document.Root.Elements(Ns + "settings").Where(element => (string?)element.Attribute("pass") == "specialize").ToArray();
        if (settings.Length > 1) throw new InvalidDataException("The answer file contains duplicate specialize passes.");
        if (settings.Any(element => element.Attributes().Any(attribute => attribute.Name.LocalName.Equals("wasPassProcessed", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("The specialize pass is already marked as processed.");
        XElement pass = settings.SingleOrDefault() ?? new XElement(Ns + "settings", new XAttribute("pass", "specialize"));
        XElement[] components = pass.Elements(Ns + "component").Where(element => (string?)element.Attribute("name") == "Microsoft-Windows-Deployment").ToArray();
        if (components.Length > 1 || components.Any(element => (string?)element.Attribute("processorArchitecture") != target))
            throw new InvalidDataException("The Windows Deployment component conflicts with the selected image architecture.");
        XElement component = components.SingleOrDefault() ?? new XElement(Ns + "component",
            new XAttribute("name", "Microsoft-Windows-Deployment"), new XAttribute("processorArchitecture", target),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"), new XAttribute("language", "neutral"), new XAttribute("versionScope", "nonSxS"));
        XElement[] lists = component.Elements(Ns + "RunSynchronous").ToArray();
        if (lists.Length > 1) throw new InvalidDataException("Duplicate RunSynchronous lists are not supported.");
        XElement list = lists.SingleOrDefault() ?? new XElement(Ns + "RunSynchronous");
        var orders = new HashSet<int>();
        foreach (XElement entry in list.Elements(Ns + "RunSynchronousCommand"))
            if (!int.TryParse((string?)entry.Element(Ns + "Order"), out int order) || order is < 1 or > 500 || !orders.Add(order))
                throw new InvalidDataException("RunSynchronous orders must be unique integers from 1 through 500.");
        XElement? owned = candidates.SingleOrDefault();
        if (owned is not null && (owned.Element(Ns + "Credentials") is not null ||
            new[] { "Order", "Path", "Description", "WillReboot" }.Any(name => owned.Elements(Ns + name).Count() > 1)))
            throw new InvalidDataException("The Foundry command must use the canonical SYSTEM execution settings.");
        if (owned is not null && owned.Parent != list) throw new InvalidDataException("The Foundry command must belong to the specialize Windows Deployment component.");
        if (owned is not null && ((string?)owned.Element(Ns + "Path") != Command ||
            (string?)owned.Element(Ns + "WillReboot") != "OnRequest" ||
            owned.Attribute(Wcm + "action") is { Value: not "add" }))
            throw new InvalidDataException("The existing Foundry command conflicts with automatic post-installation integration.");
        if (owned is null)
        {
            int order = orders.Count == 0 ? 1 : orders.Max() + 1;
            if (order > 500) throw new InvalidDataException("No RunSynchronous order remains after the existing commands.");
            owned = new XElement(Ns + "RunSynchronousCommand", new XAttribute(Wcm + "action", "add"), new XElement(Ns + "Order", order));
            list.Add(owned);
        }
        owned.SetAttributeValue(Wcm + "action", "add");
        owned.SetElementValue(Ns + "Description", Description);
        owned.SetElementValue(Ns + "Path", Command);
        owned.SetElementValue(Ns + "WillReboot", "OnRequest");
        if (list.Parent is null) component.Add(list);
        if (component.Parent is null) pass.Add(component);
        if (pass.Parent is null) document.Root.Add(pass);
    }
}
