// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Controls;

namespace Foundry.Wpf.Shell;

/// <summary>
/// Full-page status shown in the shell while a startup phase works on its own: an indeterminate ring, the title and a status
/// line that screen readers announce when it changes. Its data context is a <see cref="StatusPageModel"/>.
/// </summary>
/// <remarks>It uses only Fluent and Foundry.Wpf resources, so any host of the shell window can show it.</remarks>
public partial class StatusPageView : UserControl
{
    /// <summary>Initializes the page.</summary>
    public StatusPageView() => InitializeComponent();
}
