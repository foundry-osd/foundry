// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.ApplicationShell;

public interface IApplicationShellService
{
    void ShowAbout();

    bool ConfirmWarning(string title, string message);
}
