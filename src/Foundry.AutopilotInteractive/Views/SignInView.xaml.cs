// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Controls;

namespace Foundry.AutopilotInteractive.Views;

/// <summary>Page 1: device code sign-in. Bound to <see cref="ViewModels.SignInViewModel"/>; no logic in code-behind.</summary>
public partial class SignInView : UserControl
{
    /// <summary>Builds the page.</summary>
    public SignInView()
    {
        InitializeComponent();
    }
}
