// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Foundry.Avalonia.Controls;

namespace Foundry.Avalonia.Tests.Controls;

public sealed class AdaptiveLayoutHostTests
{
    [AvaloniaTheory]
    [InlineData(800, ":compact")]
    [InlineData(1023, ":compact")]
    [InlineData(1024, ":standard")]
    [InlineData(1279, ":standard")]
    [InlineData(1280, ":wide")]
    [InlineData(1920, ":wide")]
    public void ArrangeSelectsExactlyOneWidthPseudoClass(double width, string expectedPseudoClass)
    {
        var host = new TestAdaptiveLayoutHost();

        host.Measure(new Size(width, 768));
        host.Arrange(new Rect(0, 0, width, 768));

        Assert.True(host.HasPseudoClass(expectedPseudoClass));
        Assert.Equal(1, host.ActiveWidthPseudoClassCount);
    }

    [AvaloniaFact]
    public void RepeatedArrangeReplacesThePreviousWidthPseudoClass()
    {
        var host = new TestAdaptiveLayoutHost();

        foreach ((double width, string expectedPseudoClass) in new[]
                 {
                     (800d, ":compact"),
                     (1024d, ":standard"),
                     (1280d, ":wide"),
                     (800d, ":compact"),
                 })
        {
            host.Measure(new Size(width, 768));
            host.Arrange(new Rect(0, 0, width, 768));

            Assert.True(host.HasPseudoClass(expectedPseudoClass));
            Assert.Equal(1, host.ActiveWidthPseudoClassCount);
        }
    }

    [AvaloniaFact]
    public void WidthPseudoClassActivatesAvaloniaStyleSelectors()
    {
        var host = new TestAdaptiveLayoutHost();
        var root = new Border { Child = host };
        var wideStyle = new Style(selector => selector.Is<AdaptiveLayoutHost>().Class(":wide"));
        wideStyle.Setters.Add(new Setter(Control.TagProperty, "wide"));
        root.Styles.Add(wideStyle);
        var window = new Window
        {
            Width = 1280,
            Height = 768,
            Content = root,
        };
        window.Show();
        try
        {
            host.Measure(new Size(1280, 768));
            host.Arrange(new Rect(0, 0, 1280, 768));

            Assert.Equal("wide", host.Tag);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestAdaptiveLayoutHost : AdaptiveLayoutHost
    {
        public bool HasPseudoClass(string pseudoClass) => PseudoClasses.Contains(pseudoClass);

        public int ActiveWidthPseudoClassCount =>
            new[] { ":compact", ":standard", ":wide" }.Count(PseudoClasses.Contains);
    }
}
