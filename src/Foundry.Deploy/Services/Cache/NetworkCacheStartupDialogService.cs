// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Runtime;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Shows the blocking network cache startup failure before the wizard opens.</summary>
public interface INetworkCacheStartupDialogService
{
    /// <summary>
    /// Shows the failure on the UI thread and waits for the operator. Returns <see langword="true"/> when Retry was chosen;
    /// Retry is offered only for causes that can resolve without new boot media.
    /// </summary>
    bool ShowFailure(NetworkCacheStartupFailure failure);
}

public sealed class NetworkCacheStartupDialogService(ILocalizationService localizationService, RuntimeStartupDiagnostics startup)
    : INetworkCacheStartupDialogService
{
    /// <inheritdoc />
    public bool ShowFailure(NetworkCacheStartupFailure failure)
    {
        (string title, string message, bool canRetry) = NetworkCacheStartupMessages.Format(failure, localizationService);
        string close = localizationService.GetString("Common.Close");
        LocalizedMessageDialog dialog = canRetry
            ? new LocalizedMessageDialog(title, message, localizationService.GetString("Common.Retry"), close)
            : new LocalizedMessageDialog(title, message, close);

        // The blocking message is the usable UI of this run; Bootstrap must not report a readiness timeout while it is open.
        dialog.ContentRendered += (_, _) => startup.ReportUiReady();
        return dialog.ShowDialog() == true && canRetry;
    }
}
