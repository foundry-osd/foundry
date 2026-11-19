// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Deploy.Services.Catalog;

/// <summary>What the operator chose in the catalog startup failure dialog.</summary>
public enum CatalogStartupChoice
{
    /// <summary>Close Foundry Deploy without deploying.</summary>
    Close,

    /// <summary>Load the catalogs again.</summary>
    Retry
}

/// <summary>Shows the catalog startup failure before the wizard opens.</summary>
public interface ICatalogStartupDialogService
{
    /// <summary>Shows the failure on the UI thread, over the shell window, and waits for the operator.</summary>
    CatalogStartupChoice ShowFailure(Exception failure);
}

public sealed class CatalogStartupDialogService(ILocalizationService localizationService, RuntimeStartupDiagnostics startup)
    : ICatalogStartupDialogService
{
    /// <inheritdoc />
    public CatalogStartupChoice ShowFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var dialog = new LocalizedMessageDialog(
            localizationService.GetString("CatalogStartup.Title"),
            localizationService.GetString("CatalogStartup.Message"),
            localizationService.GetString("Common.Retry"),
            localizationService.GetString("Common.Close"));

        // The message can be Deploy's first rendered UI; the host must not wait for the shell content while it is open.
        dialog.ContentRendered += (_, _) => startup.ReportUiReady();
        return dialog.ShowDialog() == true ? CatalogStartupChoice.Retry : CatalogStartupChoice.Close;
    }
}
