// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class FirstBootLauncherServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stage_AmbiguousOrDifferentArchitecturePreservesGeneratedSource(bool duplicate)
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-firstboot-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string component = "<component name='Microsoft-Windows-Deployment' processorArchitecture='" + (duplicate ? "amd64" : "arm64") + "'><RunSynchronous/></component>";
        File.WriteAllText(path, "<unattend xmlns='urn:schemas-microsoft-com:unattend'><settings pass='specialize'>" +
            component + (duplicate ? component : string.Empty) + "</settings></unattend>");
        byte[] original = File.ReadAllBytes(path);
        try
        {
            Assert.Throws<InvalidDataException>(() => FirstBootLauncherService.Stage(root, "x64",
                new(FirstBootEntryPoint.GeneratedSpecialize, false, null), false));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Stage_GeneratedLauncherPreservesOtherCommandsAndRemainsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-firstboot-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """
                <unattend xmlns="urn:schemas-microsoft-com:unattend"><settings pass="specialize"><component name="Microsoft-Windows-Deployment" processorArchitecture="amd64"><RunSynchronous><RunSynchronousCommand><Order>3</Order><Description>Existing command</Description><Path>existing.exe</Path></RunSynchronousCommand></RunSynchronous></component></settings></unattend>
                """);
            var plan = new FirstBootExecutionPlan(FirstBootEntryPoint.GeneratedSpecialize, false, null);
            FirstBootLauncherService.Stage(root, "x64", plan, false);
            FirstBootLauncherService.Stage(root, "x64", plan, false);
            Assert.True(UnattendFileService.Inspect(File.ReadAllBytes(path), "amd64").HasFoundrySpecializeLauncher);
            XNamespace ns = "urn:schemas-microsoft-com:unattend";
            XElement[] commands = XDocument.Load(path).Descendants(ns + "RunSynchronousCommand").ToArray();
            Assert.Equal(2, commands.Length);
            Assert.Contains(commands, command => command.Element(ns + "Path")?.Value == "existing.exe" && command.Element(ns + "Order")?.Value == "3");
            Assert.Contains(commands, command => command.Element(ns + "Path")?.Value == UnattendInspection.FirstBootLauncherCommand && command.Element(ns + "Order")?.Value == "4");
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(FirstBootEntryPoint.None)]
    [InlineData(FirstBootEntryPoint.VerifiedCustomSpecialize)]
    [InlineData(FirstBootEntryPoint.SetupComplete)]
    [InlineData(FirstBootEntryPoint.OobeCommand)]
    [InlineData(FirstBootEntryPoint.GeneratedSpecialize)]
    public void Stage_CustomAnswerFileBytesNeverChange(FirstBootEntryPoint entryPoint)
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-firstboot-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Windows", "Panther", "unattend.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] original = [0xEF, 0xBB, 0xBF, 1, 2, 3];
        File.WriteAllBytes(path, original);
        try
        {
            var plan = new FirstBootExecutionPlan(entryPoint, false, null);
            if (entryPoint == FirstBootEntryPoint.GeneratedSpecialize)
                Assert.Throws<InvalidOperationException>(() => FirstBootLauncherService.Stage(root, "x64", plan, true));
            else FirstBootLauncherService.Stage(root, "x64", plan, true);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Stage_NewGeneratedAnswerFileContainsQualifiedLauncher()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-firstboot-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            FirstBootLauncherService.Stage(root, "x64", new(FirstBootEntryPoint.GeneratedSpecialize, false, null), false);
            Assert.True(UnattendFileService.Inspect(File.ReadAllBytes(Path.Combine(root, "Windows", "Panther", "unattend.xml")), "amd64").HasFoundrySpecializeLauncher);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
