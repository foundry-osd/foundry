// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Deploy.Services.Cache;

/// <summary>What the operator chose in the network cache startup failure dialog.</summary>
public enum NetworkCacheStartupChoice
{
    /// <summary>Close Foundry Deploy without deploying.</summary>
    Close,

    /// <summary>Run the network cache selection again.</summary>
    Retry,

    /// <summary>Open the wizard without the network cache and without the content published to it.</summary>
    ContinueWithout
}

/// <summary>Shows the network cache startup failure before the wizard opens.</summary>
public interface INetworkCacheStartupDialogService
{
    /// <summary>
    /// Shows the failure on the UI thread and waits for the operator. Continue without the network cache is always offered;
    /// Retry is offered only for causes that can resolve without new boot media.
    /// </summary>
    NetworkCacheStartupChoice ShowFailure(NetworkCacheStartupFailure failure);
}

public sealed class NetworkCacheStartupDialogService(ILocalizationService localizationService, RuntimeStartupDiagnostics startup)
    : INetworkCacheStartupDialogService
{
    /// <inheritdoc />
    public NetworkCacheStartupChoice ShowFailure(NetworkCacheStartupFailure failure)
    {
        (string title, string cause, bool canRetry) = NetworkCacheStartupMessages.Format(failure, localizationService);
        // The cause says what to fix; the note says what the deployment loses when the operator does not wait for that fix.
        string message = cause + Environment.NewLine + Environment.NewLine + localizationService.GetString("NetworkCacheStartup.ContinueWithoutNote");
        string close = localizationService.GetString("Common.Close");
        string continueWithout = localizationService.GetString("NetworkCacheStartup.ContinueWithout");
        LocalizedMessageDialog dialog = canRetry
            ? new LocalizedMessageDialog(title, message, localizationService.GetString("Common.Retry"), close, continueWithout)
            : new LocalizedMessageDialog(title, message, continueWithout, close);

        // The message is the usable UI of this run; Bootstrap must not report a readiness timeout while it is open.
        dialog.ContentRendered += (_, _) => startup.ReportUiReady();
        bool primary = dialog.ShowDialog() == true;
        return (canRetry, primary, dialog.IsSecondaryButtonSelected) switch
        {
            (true, true, _) => NetworkCacheStartupChoice.Retry,
            (true, false, true) or (false, true, _) => NetworkCacheStartupChoice.ContinueWithout,
            _ => NetworkCacheStartupChoice.Close
        };
    }
}
