// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Foundry.Connect.Views;

public partial class EthernetStatusPanel : UserControl
{
    public EthernetStatusPanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
