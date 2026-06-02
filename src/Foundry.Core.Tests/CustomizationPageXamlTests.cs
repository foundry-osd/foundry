using System.Xml.Linq;

namespace Foundry.Core.Tests;

public sealed class CustomizationPageXamlTests
{
    private static readonly XNamespace ToolkitNamespace = "using:CommunityToolkit.WinUI.Controls";

    [Fact]
    public void CustomizationPage_SettingsExpanderItems_DoNotContainSettingsExpanders()
    {
        string sourceRoot = FindSourceRoot();
        string xamlPath = Path.Combine(sourceRoot, "Foundry", "Views", "CustomizationPage.xaml");
        XDocument document = XDocument.Load(xamlPath);

        var nestedExpanders = document
            .Descendants(ToolkitNamespace + "SettingsExpander")
            .Elements(ToolkitNamespace + "SettingsExpander.Items")
            .Elements(ToolkitNamespace + "SettingsExpander")
            .Select(element => element.Attribute("Header")?.Value ?? "(unknown)")
            .ToArray();

        Assert.Empty(nestedExpanders);
    }

    private static string FindSourceRoot()
    {
        string? directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            string sourceRoot = Path.Combine(directory, "src");

            if (Directory.Exists(sourceRoot))
            {
                return sourceRoot;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository source root.");
    }
}
