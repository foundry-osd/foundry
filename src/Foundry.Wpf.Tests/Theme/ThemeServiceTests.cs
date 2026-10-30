// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Windows;
using Foundry.Wpf.Theme;

namespace Foundry.Wpf.Tests.Theme;

public sealed class ThemeServiceTests
{
    [Fact]
    public void Apply_ReplacesOnlyFluentDictionary()
    {
        var styles = new ResourceDictionary();
        var merged = new Collection<ResourceDictionary> { new ResourceDictionary(), styles };
        var fluent = new ResourceDictionary();

        ThemeService.ApplyFluentDictionary(merged, fluent);

        Assert.Same(fluent, merged[0]);
        Assert.Same(styles, merged[1]);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Apply_AddsFluentDictionaryWhenEmpty()
    {
        var merged = new Collection<ResourceDictionary>();
        var fluent = new ResourceDictionary();

        ThemeService.ApplyFluentDictionary(merged, fluent);

        Assert.Same(fluent, Assert.Single(merged));
    }
}
