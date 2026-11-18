// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Progress;

namespace Foundry.Wpf.Tests.Progress;

public sealed class ProgressTimelineEntryTests
{
    [Theory]
    [InlineData(ProgressStepState.Pending, "\uECCA", false)]
    [InlineData(ProgressStepState.Running, "\uE915", true)]
    [InlineData(ProgressStepState.Succeeded, "\uE930", false)]
    [InlineData(ProgressStepState.Failed, "\uEA39", true)]
    [InlineData(ProgressStepState.Skipped, "\uE946", false)]
    [InlineData(ProgressStepState.Cancelled, "\uE711", true)]
    public void State_SelectsGlyphAndActiveRow(ProgressStepState state, string glyph, bool isActive)
    {
        var entry = new ProgressTimelineEntry(1, "Apply", "Apply image", "Waiting");

        entry.Update("Apply image", state, "State");

        Assert.Equal(glyph, entry.Glyph);
        Assert.Equal(isActive, entry.IsActive);
    }

    [Fact]
    public void Update_RaisesChangesForEveryDisplayedValue()
    {
        var entry = new ProgressTimelineEntry(1, "Apply", "Apply image", "Waiting");
        var changed = new List<string?>();
        entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        entry.Update("Appliquer l'image", ProgressStepState.Running, "En cours");

        Assert.Contains(nameof(ProgressTimelineEntry.State), changed);
        Assert.Contains(nameof(ProgressTimelineEntry.IsActive), changed);
        Assert.Contains(nameof(ProgressTimelineEntry.Glyph), changed);
        Assert.Contains(nameof(ProgressTimelineEntry.DisplayName), changed);
        Assert.Contains(nameof(ProgressTimelineEntry.StateAutomationText), changed);
    }
}
