// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using Foundry.Localization;

namespace Foundry.PostInstall.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EverySupportedCultureLoadsItsOwnCompiledResources()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Foundry.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var cultures = FoundrySupportedCultures.CreateCatalog()
            .CreateOptions(CultureInfo.GetCultureInfo("en-US"), key => key);
        Assert.Equal(38, cultures.Count);
        foreach (var option in cultures)
        {
            var manager = new ResourceManager("Foundry.PostInstall.Strings.Resources", typeof(LocalizationText).Assembly);
            try
            {
                ResourceSet? resources = manager.GetResourceSet(CultureInfo.GetCultureInfo(option.Code), true, false);
                Assert.NotNull(resources);
                XDocument source = XDocument.Load(Path.Combine(directory.FullName, "Foundry.PostInstall", "Strings", option.Code, "Resources.resx"));
                foreach (XElement entry in source.Descendants("data"))
                {
                    string key = entry.Attribute("name")!.Value;
                    Assert.Equal(entry.Element("value")!.Value, resources.GetString(key));
                }
            }
            finally
            {
                manager.ReleaseAllResources();
            }
        }
    }
}
