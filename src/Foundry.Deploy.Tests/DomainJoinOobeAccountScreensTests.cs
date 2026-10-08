// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;
using Foundry.Deploy.Services.Deployment.Unattend;

namespace Foundry.Deploy.Tests;

public sealed class DomainJoinOobeAccountScreensTests
{
    private static readonly XNamespace Unattend = "urn:schemas-microsoft-com:unattend";

    [Fact]
    public void Hide_WithoutAnswerFile_CreatesTheOobeSettings()
    {
        using var workspace = new Workspace();

        DomainJoinOobeAccountScreens.Hide(workspace.RootPath, "amd64");

        XElement oobe = LoadOobe(workspace.RootPath);
        Assert.Equal("true", oobe.Element(Unattend + "HideOnlineAccountScreens")?.Value);
        Assert.Equal("true", oobe.Element(Unattend + "HideLocalAccountScreen")?.Value);
    }

    [Fact]
    public void Hide_OverridesTheGeneratedDefaultAndKeepsOtherOobeSettings()
    {
        using var workspace = new Workspace();
        DomainJoinOobeAccountScreens.Hide(workspace.RootPath, "amd64");
        string path = Path.Combine(workspace.RootPath, "Windows", "Panther", "unattend.xml");
        XDocument existing = XDocument.Load(path);
        XElement oobe = existing.Descendants(Unattend + "OOBE").Single();
        oobe.SetElementValue(Unattend + "HideOnlineAccountScreens", "false");
        oobe.SetElementValue(Unattend + "HideLocalAccountScreen", null);
        oobe.SetElementValue(Unattend + "HideEULAPage", "true");
        existing.Save(path);

        DomainJoinOobeAccountScreens.Hide(workspace.RootPath, "amd64");

        XElement result = LoadOobe(workspace.RootPath);
        Assert.Equal("true", result.Element(Unattend + "HideOnlineAccountScreens")?.Value);
        Assert.Equal("true", result.Element(Unattend + "HideLocalAccountScreen")?.Value);
        Assert.Equal("true", result.Element(Unattend + "HideEULAPage")?.Value);
    }

    private sealed class Workspace : IDisposable
    {
        public string RootPath { get; } = Directory.CreateTempSubdirectory("foundry-oobe-").FullName;
        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }

    private static XElement LoadOobe(string windowsRoot) =>
        XDocument.Load(Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml")).Descendants(Unattend + "OOBE").Single();
}
