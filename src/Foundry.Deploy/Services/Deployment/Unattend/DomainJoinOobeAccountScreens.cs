// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;

namespace Foundry.Deploy.Services.Deployment.Unattend;

/// <summary>
/// Removes the account questions from OOBE for a computer that Foundry joins to a domain, so setup ends on the
/// sign-in screen where domain accounts are used. Without it Windows still asks whether the device is personal
/// or for work and offers to create an account.
/// </summary>
/// <remarks>
/// Applies only to the answer file Foundry generates; an imported answer file keeps ownership of its OOBE section.
/// Microsoft documents <c>HideLocalAccountScreen</c> for Windows Server only; it is written for parity with the
/// answer files commonly used for domain-joined client deployments and is ignored where it does not apply.
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
