// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.WinPE.Ui;

// The namespace of the linked host from Foundry.Deploy.Tests, which this part completes.
namespace Foundry.Deploy.Tests;

internal static partial class WpfApplicationTestHost
{
    // The UI process's own application resources, so tests see what both phases run with.
    private static partial void AddResources(Application application) =>
        WinPeApplicationResources.Apply(application, WinPeApplicationResources.UiProcess);
}
