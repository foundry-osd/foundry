// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DebugCustomImageScenarioTests
{
    [Theory]
    [InlineData(DebugCustomImageScenario.SingleIndex, 1, "")]
    [InlineData(DebugCustomImageScenario.PreferredIndex, 9, "")]
    [InlineData(DebugCustomImageScenario.MissingImage, null, "CustomImages.MissingDefault")]
    [InlineData(DebugCustomImageScenario.MissingIndex, null, "CustomImages.MissingDefault")]
    [InlineData(DebugCustomImageScenario.AmbiguousDefault, null, "CustomImages.AmbiguousDefault")]
    public void ScenarioDefaultsExerciseNormalSelectionRules(DebugCustomImageScenario scenario, int? expectedIndex, string expectedError)
    {
        DebugCustomImageSnapshot snapshot = DebugCustomImageScenarios.Create(scenario);
        using var model = new CustomImageSelectionViewModel();
        model.Configure(snapshot.Settings);
        model.ApplyCatalog(snapshot.Images);
        if (model.SelectedAsset is not null) model.ApplyIndexes(snapshot.Indexes);

        Assert.Equal(expectedIndex, model.Selection?.Index.Index);
        Assert.Equal(expectedError, model.ErrorKey);
    }

#if !DEBUG
    [Fact]
    public async Task ReleaseBuildRejectsScenarioOverride()
    {
        using var model = new CustomImageSelectionViewModel();
        model.Configure(new() { IsEnabled = false });

        await model.SetDebugScenarioAsync(DebugCustomImageScenario.PreferredIndex);

        Assert.False(model.IsDebugScenarioActive);
        Assert.False(model.IsEnabled);
        Assert.False(model.IsCustom);
        Assert.Null(model.Selection);
    }
#endif
}
