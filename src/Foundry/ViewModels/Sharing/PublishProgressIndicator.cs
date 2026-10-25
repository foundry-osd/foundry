// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Localization;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Follows the publish a dialog started: its stage with the item name and its percentage, from the progress the publication engine
/// reports through <see cref="ConfigurationSharingService"/>; the service already coalesces the reports to one UI update at a time.
/// </summary>
public sealed partial class PublishProgressIndicator(ConfigurationSharingService sharing, IApplicationLocalizationService localization)
    : ObservableObject, IDisposable
{
    private ContentProgress? previousRun;
    private bool isRunning;

    /// <summary>Gets the percentage of the running publish, from 0 to 100.</summary>
    public double Percent => isRunning ? PublishProgress.Percent(Current) : 0;

    /// <summary>Gets <see cref="Percent"/> formatted for the current UI culture; empty while <see cref="IsIndeterminate"/>.</summary>
    public string PercentText => IsIndeterminate ? string.Empty : (Percent / 100).ToString("P0", CultureInfo.CurrentUICulture);

    /// <summary>Gets whether the bar has no percentage yet: the folder is still being checked.</summary>
    public bool IsIndeterminate => isRunning && PublishProgress.Stage(Current) == PublishStage.CheckingFolder;

    /// <summary>Gets the localized stage of the running publish, with the item it handles.</summary>
    public string StageText
    {
        get
        {
            if (!isRunning) return string.Empty;
            ContentProgress? current = Current;
            return PublishProgress.Stage(current) switch
            {
                PublishStage.Verifying => localization.FormatString("Sharing.PublishStepVerifying", current!.CurrentName),
                PublishStage.Preparing => localization.FormatString("Sharing.PublishStepPreparing", current!.CurrentName),
                PublishStage.Sending => localization.FormatString("Sharing.PublishStepSending", current!.CurrentName),
                PublishStage.Finishing => localization.GetString("Sharing.PublishStepFinishing"),
                _ => localization.GetString("Sharing.PublishStepCheckingFolder")
            };
        }
    }

    /// <summary>Gets the last report of the running publish; the last report of an earlier publish or fetch counts as none.</summary>
    private ContentProgress? Current => ReferenceEquals(sharing.Progress, previousRun) ? null : sharing.Progress;

    /// <summary>Starts following a publish.</summary>
    public void Start()
    {
        if (isRunning) return;
        previousRun = sharing.Progress;
        isRunning = true;
        sharing.Changed += OnSharingChanged;
        Refresh();
    }

    /// <summary>Stops following the publish.</summary>
    public void Stop()
    {
        if (!isRunning) return;
        isRunning = false;
        sharing.Changed -= OnSharingChanged;
        Refresh();
    }

    public void Dispose() => Stop();

    private void OnSharingChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(StageText));
    }
}
