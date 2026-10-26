// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Controls;

/// <summary>Banner shown on every configuration page while a followed publication manages the configuration.</summary>
public sealed partial class ManagedConfigurationBanner : UserControl
{
    public ManagedConfigurationBanner()
    {
        InitializeComponent();
    }

    /// <summary>Gets the shared read-only state.</summary>
    public ManagedConfigurationViewModel ViewModel { get; } = App.GetService<ManagedConfigurationViewModel>();
}
