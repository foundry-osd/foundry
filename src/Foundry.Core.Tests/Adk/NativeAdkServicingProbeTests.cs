// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;

namespace Foundry.Core.Tests.Adk;

public sealed class NativeAdkServicingProbeTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void Detect_WhenEachRequiredComponentIsAppliedOrSuperseded_VerifiesServicing(string state)
    {
        var queriedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AdkServicingState result = NativeAdkServicingProbe.Detect((patch, product) =>
        {
            Assert.True(Guid.TryParse(patch, out _));
            queriedProducts.Add(product);
            return (0u, state);
        });

        Assert.Equal(AdkServicingState.Verified, result);
        Assert.Equal(3, queriedProducts.Count);
        Assert.Contains("{765CB4D6-1A08-1F46-81D0-7016DA3604B2}", queriedProducts);
        Assert.Contains("{9BB0E43E-2F04-F989-F188-F787570FD478}", queriedProducts);
        Assert.Contains("{AA0852D8-D1C3-5D2E-34B2-282A4F10036E}", queriedProducts);
    }

    [Theory]
    [InlineData(0u, "4", AdkServicingState.NotVerified)]
    [InlineData(1647u, "", AdkServicingState.NotVerified)]
    [InlineData(1605u, "", AdkServicingState.NotVerified)]
    [InlineData(5u, "1", AdkServicingState.Unknown)]
    [InlineData(234u, "1", AdkServicingState.Unknown)]
    [InlineData(0u, "", AdkServicingState.Unknown)]
    [InlineData(0u, "3", AdkServicingState.Unknown)]
    public void Detect_WhenOneRequiredComponentIsNotVerified_DoesNotTrustOthers(uint error, string state, AdkServicingState expected)
    {
        AdkServicingState result = NativeAdkServicingProbe.Detect((_, product) =>
            product == "{AA0852D8-D1C3-5D2E-34B2-282A4F10036E}" ? (error, state) : (0u, "1"));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Detect_WhenNativeQueryIsUnavailable_ReturnsUnknown()
    {
        Assert.Equal(AdkServicingState.Unknown,
            NativeAdkServicingProbe.Detect((_, _) => throw new DllNotFoundException()));
    }
}
