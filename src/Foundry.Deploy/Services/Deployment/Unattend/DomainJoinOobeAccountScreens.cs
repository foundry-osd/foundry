// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Removes the Microsoft account sign-in from OOBE for a computer that Foundry joins to a domain, so Windows no
/// longer asks whether the device is personal or for work.
/// </summary>
/// <remarks>
/// Applies only to the answer file Foundry generates; an imported answer file keeps ownership of its OOBE section.
/// Microsoft documents <c>HideLocalAccountScreen</c> for Windows Server only: on Windows client it is ignored, and
/// setup still asks who will use the device. That page is skipped by the post-installation runtime instead, and
/// only after it has verified the domain membership, so a failed join still lets someone create an account.
/// </remarks>
internal static class DomainJoinOobeAccountScreens
{
    /// <summary>Writes the two settings into the oobeSystem pass, keeping every other OOBE setting already present.</summary>
    public static void Hide(string windowsPartitionRoot, string processorArchitecture)
    {
        var documents = new UnattendDocumentService();
        XNamespace unattend = UnattendDocumentService.Namespace;
        XDocument document = documents.LoadOrCreate(windowsPartitionRoot);
        XElement component = documents.EnsureShellSetupComponent(document, "oobeSystem", processorArchitecture);
        XElement? oobe = component.Element(unattend + "OOBE");
        if (oobe is null)
        {
            oobe = new XElement(unattend + "OOBE");
            component.Add(oobe);
        }

        oobe.SetElementValue(unattend + "HideOnlineAccountScreens", "true");
        oobe.SetElementValue(unattend + "HideLocalAccountScreen", "true");
        documents.Save(windowsPartitionRoot, document);
    }
}
