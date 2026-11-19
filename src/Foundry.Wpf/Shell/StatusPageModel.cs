// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Foundry.Wpf.Shell;

/// <summary>
/// Data context of <see cref="StatusPageView"/>: a title and a status line shown while a startup phase works without any
/// operator interaction. The caller owns and localizes both texts.
/// </summary>
public sealed partial class StatusPageModel : ObservableObject
{
    /// <summary>Initializes the model with its texts.</summary>
    /// <param name="title">The page title.</param>
    /// <param name="status">The first status line.</param>
    public StatusPageModel(string title, string status)
    {
        Title = title;
        this.status = status;
    }

    /// <summary>Gets the page title.</summary>
    public string Title { get; }

    /// <summary>Gets or sets the status line; changes are announced to screen readers by the page.</summary>
    [ObservableProperty]
    private string status;

    /// <summary>
    /// Completes when both <paramref name="work"/> and the <paramref name="minimum"/> display time are done, so that a page
    /// shown for a fast step does not flash.
    /// </summary>
    /// <param name="work">The work the page covers; its failure or cancellation surfaces after the minimum time.</param>
    /// <param name="minimum">The minimum time the page stays visible, counted from this call.</param>
    /// <param name="time">The clock used to wait.</param>
    public static Task EnsureMinimumDisplayAsync(Task work, TimeSpan minimum, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(time);
        return Task.WhenAll(work, Task.Delay(minimum, time));
    }
}
