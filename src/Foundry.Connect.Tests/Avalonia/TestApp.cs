// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Foundry.Connect.Tests.Avalonia;

internal sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new StyleInclude(new Uri("avares://Foundry.Connect.Tests/"))
        {
            Source = new Uri("avares://Foundry.Avalonia/Themes/FoundryTheme.axaml")
        });
    }
}
