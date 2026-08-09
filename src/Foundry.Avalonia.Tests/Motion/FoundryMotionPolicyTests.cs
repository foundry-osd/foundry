// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Foundry.Avalonia.Services.Motion;

namespace Foundry.Avalonia.Tests.Motion;

public sealed class FoundryMotionPolicyTests
{
    [Theory]
    [InlineData(false, true, FoundryMotionMode.Full)]
    [InlineData(true, true, FoundryMotionMode.Reduced)]
    [InlineData(false, false, FoundryMotionMode.None)]
    [InlineData(true, false, FoundryMotionMode.None)]
    public void EnvironmentAndOperatingSystemPreferenceSelectTheDefaultMode(
        bool isWinPe,
        bool isOperatingSystemAnimationEnabled,
        FoundryMotionMode expectedMode)
    {
        var policy = new FoundryMotionPolicy(isWinPe, isOperatingSystemAnimationEnabled);

        Assert.Equal(expectedMode, policy.Mode);
        Assert.Equal(expectedMode != FoundryMotionMode.None, policy.IsAnimationEnabled);
    }

    [Theory]
    [InlineData(FoundryMotionMode.Full)]
    [InlineData(FoundryMotionMode.Reduced)]
    [InlineData(FoundryMotionMode.None)]
    public void ExplicitOverrideWinsOverEnvironmentDefaults(FoundryMotionMode overrideMode)
    {
        var policy = new FoundryMotionPolicy(
            isWinPe: true,
            isOperatingSystemAnimationEnabled: false,
            overrideMode);

        Assert.Equal(overrideMode, policy.Mode);
        Assert.Equal(overrideMode != FoundryMotionMode.None, policy.IsAnimationEnabled);
    }

    [AvaloniaFact]
    public void SharedThemeExposesMotionDurationsAndDistance()
    {
        Application application = Assert.IsType<TestApp>(Application.Current);

        AssertResource(application, "FoundryMotion.Duration.Full", TimeSpan.FromMilliseconds(160));
        AssertResource(application, "FoundryMotion.Duration.Reduced", TimeSpan.FromMilliseconds(90));
        AssertResource(application, "FoundryMotion.Duration.None", TimeSpan.Zero);
        AssertResource(application, "FoundryMotion.Distance.Full", 16d);
        AssertResource(application, "FoundryMotion.Distance.Reduced", 0d);
        AssertResource(application, "FoundryMotion.Distance.None", 0d);
    }

    private static void AssertResource<T>(Application application, string key, T expected)
    {
        Assert.True(application.TryGetResource(key, ThemeVariant.Default, out object? value));
        Assert.Equal(expected, Assert.IsType<T>(value));
    }
}
