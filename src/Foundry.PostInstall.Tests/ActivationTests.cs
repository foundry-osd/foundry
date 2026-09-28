// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.PostInstall.Actions;

namespace Foundry.PostInstall.Tests;

public sealed class ActivationTests
{
    [Theory]
    [InlineData("Retail", "OTHER", 0, false)]
    [InlineData("Volume:GVLK", "3V66T", 0, false)]
    [InlineData("Retail", "3V66T", 1, false)]
    [InlineData("Retail", "3V66T", 0, true)]
    public void Activation_PreservesAdministratorKeysAndExistingLicenses(string channel, string suffix, int status, bool install)
    {
        var licensing = new FakeLicensing(new("license", "Professional", status, channel, suffix, "", ""));
        ActivationResult result = new OemActivation(licensing).Run();
        Assert.False(result.Failed);
        Assert.Equal(install ? 1 : 0, licensing.InstallCalls);
        Assert.Equal(install ? 1 : 0, licensing.ActivateCalls);
    }

    [Fact]
    public void Activation_FirmwareEditionMismatchDoesNotReplaceKey()
    {
        var licensing = new FakeLicensing(new("license", "Professional", 0, "Retail", "3V66T", "", ""))
        { Firmware = new("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", "Windows Core OEM:DM", "", false) };
        Assert.False(new OemActivation(licensing).Run().Failed);
        Assert.Equal(0, licensing.InstallCalls);
    }

    private sealed class FakeLicensing(LicensingProduct product) : IWindowsLicensing
    {
        public int InstallCalls { get; private set; }
        public int ActivateCalls { get; private set; }
        public bool IsClient => true;
        public string Edition => "Professional";
        public FirmwareLicense Firmware { get; set; } = new("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", "Windows Professional OEM:DM", "", false);
        public IReadOnlyList<LicensingProduct> GetProducts() => [product];
        public void InstallKey(string key) { InstallCalls++; product = product with { Channel = "OEM:DM", PartialKey = "EEEEE" }; }
        public void Refresh() { }
        public void Activate(string id) { ActivateCalls++; product = product with { LicenseStatus = 1 }; }
    }
}
