// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Shell;

/// <summary>Window-level services a WPF application exposes to its view models.</summary>
public interface IApplicationShellService
{
    /// <summary>Shows the modal About dialog for the hosting application.</summary>
    void ShowAbout();

    /// <summary>Shows a modal Yes/No warning and returns <see langword="true"/> when the operator confirmed.</summary>
    bool ConfirmWarning(string title, string message);

    /// <summary>Shuts the WPF application down.</summary>
    /// <remarks>
    /// The application ends with exit code 0. Foundry.Connect returns a meaningful exit code to Foundry.Bootstrap, so it must
    /// exit only through its lifetime service (<c>TryExit</c>), never through this method.
    /// </remarks>
    void Shutdown();
}
