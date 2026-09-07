// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeCapabilityValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsActualInstalledIdentities()
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        var result = await ValidateAsync(fixture);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains("WinPE-PowerShell-Package~31bf3856ad364e35~amd64~~10.0.26100.1", result.Value!.InstalledPackageIdentities);
        Assert.Contains("WinPE-Dot3Svc-Package@en-us", result.Value.RequiredPackages);
        Assert.Equal(21, result.Value.InstalledPackageIdentities.Count);
    }

    [Theory]
    [InlineData("WinPE-PowerShell-Package", "WinPE-PowerShell-Extra-Package")]
    [InlineData("~amd64~", "~arm64~")]
    [InlineData("~31bf3856ad364e35~", "~0000000000000000~")]
    [InlineData("~en-us~", "~fr-fr~")]
    public async Task ValidateAsync_RejectsWrongIdentityFields(string oldValue, string replacement)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        fixture.Runner.TransformInventory = output => output.Replace(oldValue, replacement, StringComparison.Ordinal);
        Assert.False((await ValidateAsync(fixture)).IsSuccess);
    }

    [Fact]
    public async Task ValidateAsync_RejectsSatelliteWithDifferentInstalledVersion()
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        fixture.Runner.TransformInventory = output => output.Replace("~en-us~10.0.26100.1", "~en-us~10.0.22621.1", StringComparison.Ordinal);
        Assert.False((await ValidateAsync(fixture)).IsSuccess);
    }

    [Theory]
    [InlineData("Installed", true)]
    [InlineData("Install Pending", false)]
    public async Task ValidateAsync_ResolvesConflictingStateWithExactPackageInfo(string state, bool succeeds)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        const string identity = "WinPE-PowerShell-Package~31bf3856ad364e35~amd64~~10.0.26100.1";
        fixture.Runner.TransformInventory = output => output + $"\nPackage Identity : {identity}\nState : Install Pending\n";
        fixture.Runner.PackageInfoOutput = $"Package Identity : {identity}\nState : {state}\nRelease Type : Feature Pack\n";
        var result = await ValidateAsync(fixture);
        Assert.Equal(succeeds, result.IsSuccess);
        Assert.Contains(fixture.Runner.Calls, args => args.Contains("/English") && args.Contains("/Get-PackageInfo") && args.Contains($"/PackageName:{identity}"));
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("duplicate-state")]
    [InlineData("long-line")]
    [InlineData("large-output")]
    [InlineData("empty")]
    public async Task ValidateAsync_RejectsIncompleteOrExcessiveInventory(string mode)
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        fixture.Runner.TransformInventory = output => mode switch
        {
            "missing-state" => output.Replace("State : Installed", "Other : Installed", StringComparison.Ordinal),
            "duplicate-state" => output.Replace("State : Installed", "State : Installed\nState : Installed", StringComparison.Ordinal),
            "long-line" => output + new string('x', 4097),
            "large-output" => output + new string('x', 4 * 1024 * 1024),
            _ => string.Empty
        };
        Assert.False((await ValidateAsync(fixture)).IsSuccess);
    }

    [Fact]
    public async Task ValidateAsync_UnrelatedPublisherDoesNotInvalidateRequiredMicrosoftPackages()
    {
        using var fixture = new InternationalizationFixture();
        fixture.Runner.InstallAll();
        fixture.Runner.Installed.Add("Vendor-Diagnostics-Package~0123456789abcdef~amd64~~1.0.0.0");
        Assert.True((await ValidateAsync(fixture)).IsSuccess);
    }
    private static Task<WinPeResult<WinPeCapabilityValidationResult>> ValidateAsync(InternationalizationFixture fixture) =>
        new WinPeCapabilityValidationService(fixture.Runner).ValidateAsync(fixture.Options, TestContext.Current.CancellationToken);
}
