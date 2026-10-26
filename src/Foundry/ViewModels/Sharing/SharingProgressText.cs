// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Localized step and percentage texts of the publish and fetch progress, shared by the sharing dialogs, the card and media creation.</summary>
internal static class SharingProgressText
{
    /// <summary>Returns the step of a publish with the item it handles, for example "Sending Win11.wim…".</summary>
    public static string Publish(ContentProgress? progress, IApplicationLocalizationService localization) => PublishProgress.Stage(progress) switch
    {
        PublishStage.Verifying => localization.FormatString("Sharing.PublishStepVerifying", progress!.CurrentName),
        PublishStage.Preparing => localization.FormatString("Sharing.PublishStepPreparing", progress!.CurrentName),
        PublishStage.Sending => localization.FormatString("Sharing.PublishStepSending", progress!.CurrentName),
        PublishStage.Finishing => localization.GetString("Sharing.PublishStepFinishing"),
        _ => localization.GetString("Sharing.PublishStepCheckingFolder")
    };

    /// <summary>Returns the step of a fetch with the item it handles, for example "Installing Drivers2…"; empty before the first report and once complete.</summary>
    public static string Sync(ContentProgress? progress, IApplicationLocalizationService localization) => progress is null
        ? string.Empty
        : SyncProgress.Stage(progress) switch
        {
            SyncStage.Downloading => localization.FormatString("Sharing.SyncStepDownloading", progress.CurrentName),
            SyncStage.Installing => localization.FormatString("Sharing.SyncStepInstalling", progress.CurrentName),
            _ => string.Empty
        };

    /// <summary>Formats a percentage from 0 to 100 for the current UI culture, for example "42 %".</summary>
    public static string Percent(double percent) => (percent / 100).ToString("P0", CultureInfo.CurrentUICulture);
}
