// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.Deployment.Unattend;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Adds the qualified launcher only to a generated answer file; custom bytes remain untouched.</summary>
public static class FirstBootLauncherService
{
    public static void Stage(string targetRoot, string architecture, FirstBootExecutionPlan plan, bool usesCustomUnattend)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CustomizationEntryPoint != FirstBootEntryPoint.GeneratedSpecialize) return;
        if (usesCustomUnattend || plan.FailureCode is not null)
            throw new InvalidOperationException("A generated first-boot launcher cannot modify a custom or unqualified answer file.");
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        string normalizedArchitecture = architecture.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" => "amd64",
            "arm64" or "aarch64" => "arm64",
            "x86" => "x86",
            _ => throw new InvalidOperationException("The target architecture is unsupported for first-boot staging.")
        };
        var builder = new UnattendDocumentService();
        XDocument document = builder.LoadOrCreate(targetRoot);
        XNamespace ns = UnattendDocumentService.Namespace;
        XElement[] existingComponents = document.Root?.Elements(ns + "settings")
            .Where(settings => string.Equals((string?)settings.Attribute("pass"), "specialize", StringComparison.OrdinalIgnoreCase))
            .Elements(ns + "component")
            .Where(entry => string.Equals((string?)entry.Attribute("name"), "Microsoft-Windows-Deployment", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (existingComponents.Length > 1 || (existingComponents.Length == 1 &&
            !string.Equals((string?)existingComponents[0].Attribute("processorArchitecture"), normalizedArchitecture, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Generated deployment components do not match the target architecture unambiguously.");
        XElement component = builder.EnsureDeploymentComponent(document, "specialize", normalizedArchitecture);
        XElement[] groups = component.Elements(ns + "RunSynchronous").ToArray();
        if (groups.Length > 1) throw new InvalidDataException("Generated synchronous command groups are ambiguous.");
        XElement group = groups.SingleOrDefault() ?? new XElement(ns + "RunSynchronous");
        if (group.Parent is null) component.Add(group);
        group.Elements(ns + "RunSynchronousCommand").Where(command =>
            command.Element(ns + "Description")?.Value == UnattendInspection.FirstBootLauncherDescription).Remove();
        var orders = new HashSet<int>();
        foreach (XElement command in group.Elements(ns + "RunSynchronousCommand"))
        {
            XElement[] values = command.Elements(ns + "Order").ToArray();
            if (values.Length != 1 || !int.TryParse(values[0].Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int order) ||
                order is < 1 or > 500 || !orders.Add(order) ||
                command.Element(ns + "Path")?.Value == UnattendInspection.FirstBootLauncherCommand)
                throw new InvalidDataException("Generated synchronous commands have invalid or duplicate order or launcher metadata.");
        }
        int nextOrder = orders.Count == 0 ? 1 : orders.Max() + 1;
        if (nextOrder > 500) throw new InvalidDataException("Generated synchronous command ordering has no remaining slot.");
        group.Add(new XElement(ns + "RunSynchronousCommand",
            new XAttribute(XNamespace.Get("http://schemas.microsoft.com/WMIConfig/2002/State") + "action", "add"),
            new XElement(ns + "Order", nextOrder.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "Description", UnattendInspection.FirstBootLauncherDescription),
            new XElement(ns + "Path", UnattendInspection.FirstBootLauncherCommand)));
        builder.Save(targetRoot, document);
    }
}
