// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.Configuration;

public sealed class WindowsOptionalFeatureCompatibilityEvaluatorTests
{
    [Theory]
    [InlineData("Home", WindowsOptionalFeatureCompatibility.Unavailable)]
    [InlineData("Pro", WindowsOptionalFeatureCompatibility.Available)]
    public void Evaluate_HyperV_UsesDocumentedEditionRestriction(
        string edition,
        WindowsOptionalFeatureCompatibility expected)
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:microsoft-hyper-v-all",
            [edition],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Evaluate_HyperVChild_InheritsParentRestriction()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:microsoft-hyper-v-hypervisor",
            ["Home"],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.Unavailable, actual);
    }

    [Fact]
    public void Evaluate_MixedSupportedAndUnsupportedEditions_ReturnsPartiallyAvailable()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:containers-disposableclientvm",
            ["Home", "Pro"],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.PartiallyAvailable, actual);
    }

    [Theory]
    [InlineData("25H2", WindowsOptionalFeatureCompatibility.Available)]
    [InlineData("unknown", WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired)]
    public void Evaluate_NetFx3_UsesKnownReleaseBuild(
        string releaseId,
        WindowsOptionalFeatureCompatibility expected)
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:netfx3",
            ["Pro"],
            [releaseId],
            WinPeArchitecture.X64);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(27999, WindowsOptionalFeatureCompatibility.Available)]
    [InlineData(28000, WindowsOptionalFeatureCompatibility.Unavailable)]
    public void EvaluateBuilds_NetFx3_UsesPayloadBuildBoundary(
        int build,
        WindowsOptionalFeatureCompatibility expected)
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.EvaluateBuilds(
            "wf:netfx3",
            ["Pro"],
            [build],
            WinPeArchitecture.X64);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Evaluate_EmptyTargets_RequiresRuntimeVerification()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:microsoft-hyper-v-all",
            [],
            [],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired, actual);
    }

    [Fact]
    public void Evaluate_UnknownEdition_RequiresRuntimeVerification()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:microsoft-hyper-v-all",
            ["Unknown"],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired, actual);
    }

    [Fact]
    public void Evaluate_FeatureWithoutDocumentedRestrictions_RequiresRuntimeVerification()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:telnetclient",
            ["Pro"],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired, actual);
    }

    [Fact]
    public void Evaluate_UnknownFeature_ReturnsUnavailable()
    {
        WindowsOptionalFeatureCompatibility actual = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
            "wf:unknown",
            ["Pro"],
            ["25H2"],
            WinPeArchitecture.X64);

        Assert.Equal(WindowsOptionalFeatureCompatibility.Unavailable, actual);
    }
}
