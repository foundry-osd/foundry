// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Views;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentPasswordDialogService(ILocalizationService localizationService)
    : IDeploymentPasswordDialogService
{
    public DeploymentPasswordPromptResult Prompt(bool previousAttemptFailed)
    {
        var dialog = new DeploymentPasswordDialog
        {
            Title = localizationService.Strings["DeploymentAccess.Title"],
            PromptText = localizationService.Strings["DeploymentAccess.Prompt"],
            PasswordPlaceholder = localizationService.Strings["DeploymentAccess.PasswordPlaceholder"],
            UnlockText = localizationService.Strings["DeploymentAccess.Unlock"],
            CancelText = localizationService.Strings["Common.Cancel"],
            ErrorText = previousAttemptFailed
                ? localizationService.Strings["DeploymentAccess.InvalidPassword"]
                : string.Empty,
            Owner = ResolveOwnerWindow()
        };

        bool? submitted = dialog.ShowDialog();
        if (submitted != true)
        {
            return DeploymentPasswordPromptResult.Cancelled();
        }

        return DeploymentPasswordPromptResult.SubmittedOwned(dialog.TakePassword());
    }

    private static Window? ResolveOwnerWindow()
    {
        if (Application.Current?.Windows is not null)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsActive)
                {
                    return window;
                }
            }
        }

        return Application.Current?.MainWindow;
    }
}
