// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Sharing;

namespace Foundry.ViewModels;

/// <summary>
/// Follows the publish a dialog started as a determinate percentage, from the content progress the publication engine reports
/// through <see cref="ConfigurationSharingService"/>; the service already coalesces the reports to one UI update at a time.
/// </summary>
public sealed partial class PublishProgressIndicator(ConfigurationSharingService sharing) : ObservableObject, IDisposable
{
    private ContentProgress? previousRun;
    private bool isRunning;

    /// <summary>Gets the percentage of the running publish, from 0 to 100.</summary>
    public double Percent =>
        isRunning ? PublishProgress.Percent(ReferenceEquals(sharing.Progress, previousRun) ? null : sharing.Progress) : 0;

    /// <summary>Gets <see cref="Percent"/> formatted for the current UI culture.</summary>
    public string PercentText => (Percent / 100).ToString("P0", CultureInfo.CurrentUICulture);

    /// <summary>Starts following a publish; the last report of an earlier publish or fetch counts as no progress yet.</summary>
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
    }
}
