// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Foundry.Avalonia.Controls;

[PseudoClasses(":compact", ":standard", ":wide")]
public class AdaptiveLayoutHost : ContentControl
{
    public const double StandardMinimumWidth = 1024;
    public const double WideMinimumWidth = 1280;

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateWidthPseudoClasses(finalSize.Width);
        return base.ArrangeOverride(finalSize);
    }

    private void UpdateWidthPseudoClasses(double width)
    {
        PseudoClasses.Set(":compact", width < StandardMinimumWidth);
        PseudoClasses.Set(":standard", width >= StandardMinimumWidth && width < WideMinimumWidth);
        PseudoClasses.Set(":wide", width >= WideMinimumWidth);
    }
}
