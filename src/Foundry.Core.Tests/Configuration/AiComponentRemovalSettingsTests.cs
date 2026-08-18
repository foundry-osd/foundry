// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class AiComponentRemovalSettingsTests
{
    [Fact]
    public void HasAnyAction_WithNoSelectedAction_ReturnsFalse()
    {
        Assert.False(new AiComponentRemovalSettings().HasAnyAction());
    }

    [Theory]
    [MemberData(nameof(SelectedActions))]
    public void HasAnyAction_WithSelectedAction_ReturnsTrue(AiComponentRemovalSettings settings)
    {
        Assert.True(settings.HasAnyAction());
    }

    public static TheoryData<AiComponentRemovalSettings> SelectedActions => new()
    {
        new AiComponentRemovalSettings { RemoveCopilot = true },
        new AiComponentRemovalSettings { RemoveAiHub = true },
        new AiComponentRemovalSettings { DisableRecall = true },
        new AiComponentRemovalSettings { DisableClickToDo = true },
        new AiComponentRemovalSettings { DisableAiServiceAutoStart = true },
        new AiComponentRemovalSettings { DisableEdgeAi = true },
        new AiComponentRemovalSettings { DisablePaintAi = true },
        new AiComponentRemovalSettings { DisableNotepadAi = true }
    };
}
