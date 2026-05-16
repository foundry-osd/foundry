using System.IO;
using System.Xml.Linq;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Loads, creates, and updates the offline Windows unattend document used by deployment customization steps.
/// </summary>
internal sealed class UnattendDocumentService
{
    /// <summary>
    /// Defines the Windows unattend XML namespace used by generated answer files.
    /// </summary>
    public const string NamespaceUri = "urn:schemas-microsoft-com:unattend";

    private const string UnattendFileName = "unattend.xml";
    private const string ShellSetupComponentName = "Microsoft-Windows-Shell-Setup";
    private const string ShellSetupPublicKeyToken = "31bf3856ad364e35";
    private const string ShellSetupLanguage = "neutral";
    private const string ShellSetupVersionScope = "nonSxS";

    /// <summary>
    /// Provides the XML namespace used by unattend document elements.
    /// </summary>
    public static readonly XNamespace Namespace = NamespaceUri;

    /// <summary>
    /// Loads an existing unattend.xml from the offline Windows installation or creates a new document.
    /// </summary>
    public XDocument LoadOrCreate(string windowsPartitionRoot)
    {
        string unattendPath = GetUnattendPath(windowsPartitionRoot);
        return File.Exists(unattendPath)
            ? XDocument.Load(unattendPath, LoadOptions.PreserveWhitespace)
            : new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(Namespace + "unattend"));
    }

    /// <summary>
    /// Ensures the Shell-Setup component exists in the requested unattend pass.
    /// </summary>
    public XElement EnsureShellSetupComponent(XDocument document, string passName, string processorArchitecture)
    {
        XElement root = EnsureUnattendRoot(document);
        XElement settings = root
            .Elements(Namespace + "settings")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("pass"), passName, StringComparison.OrdinalIgnoreCase))
            ?? new XElement(Namespace + "settings", new XAttribute("pass", passName));

        if (settings.Parent is null)
        {
            root.Add(settings);
        }

        XElement component = settings
            .Elements(Namespace + "component")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), ShellSetupComponentName, StringComparison.OrdinalIgnoreCase))
            ?? new XElement(Namespace + "component");

        if (component.Parent is null)
        {
            settings.Add(component);
        }

        component.SetAttributeValue("name", ShellSetupComponentName);
        component.SetAttributeValue("processorArchitecture", NormalizeProcessorArchitecture(processorArchitecture));
        component.SetAttributeValue("publicKeyToken", ShellSetupPublicKeyToken);
        component.SetAttributeValue("language", ShellSetupLanguage);
        component.SetAttributeValue("versionScope", ShellSetupVersionScope);

        return component;
    }

    /// <summary>
    /// Saves unattend.xml into the offline Windows Panther directory.
    /// </summary>
    public void Save(string windowsPartitionRoot, XDocument document)
    {
        string unattendPath = GetUnattendPath(windowsPartitionRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(unattendPath)!);
        document.Declaration ??= new XDeclaration("1.0", "utf-8", "yes");
        document.Save(unattendPath);
    }

    private static string GetUnattendPath(string windowsPartitionRoot)
    {
        return Path.Combine(windowsPartitionRoot, "Windows", "Panther", UnattendFileName);
    }

    private static XElement EnsureUnattendRoot(XDocument document)
    {
        if (document.Root is null)
        {
            XElement root = new(Namespace + "unattend");
            document.Add(root);
            return root;
        }

        if (document.Root.Name == Namespace + "unattend")
        {
            return document.Root;
        }

        XNode[] existingNodes = document.Root.Nodes().ToArray();
        XElement replacementRoot = new(Namespace + "unattend");
        foreach (XNode node in existingNodes)
        {
            replacementRoot.Add(node);
        }

        document.Root.ReplaceWith(replacementRoot);
        return replacementRoot;
    }

    private static string NormalizeProcessorArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "amd64",
            "amd64" => "amd64",
            "x64" => "amd64",
            "arm64" => "arm64",
            "x86" => "x86",
            _ => normalized
        };
    }
}
