// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Wpf.Localization;

namespace Foundry.Wpf.Tests.Localization;

public sealed class ResourceLocalizationServiceTests
{
    [Fact]
    public void GetString_UsesInjectedResourceManager()
    {
        var manager = new TestResourceManager(new Dictionary<string, string> { ["App.Name"] = "Foundry Test" });
        var service = new ResourceLocalizationService(manager);

        Assert.Equal("Foundry Test", service.GetString("App.Name"));
    }

    private sealed class TestResourceManager : ResourceManager
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestResourceManager(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override string? GetString(string name, CultureInfo? culture)
        {
            return _values.TryGetValue(name, out string? value) ? value : null;
        }
    }
}
