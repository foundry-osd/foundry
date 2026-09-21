// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Tests;

public sealed class WindowsOemActivationScriptTests
{
    [Theory]
    [InlineData("default", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("home", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("home-n", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("home-single-language", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("home-country-specific", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("pro-n", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("addon", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    [InlineData("already-oem", "Windows OEM activation succeeded.", "Activate,RefreshLicenseStatus")]
    [InlineData("activated", "already activated", "")]
    [InlineData("volume", "volume licensing", "")]
    [InlineData("mak", "volume licensing", "")]
    [InlineData("custom-retail", "existing product key", "")]
    [InlineData("no-key", "no firmware key", "")]
    [InlineData("incompatible", "firmware edition", "")]
    [InlineData("unknown-description", "firmware edition", "")]
    [InlineData("similar-edition", "firmware edition", "")]
    [InlineData("configured-kms", "volume licensing", "")]
    [InlineData("invalid-key", "firmware key format", "")]
    [InlineData("unknown-edition", "unsupported Windows edition", "")]
    [InlineData("server", "Windows client", "")]
    [InlineData("ambiguous", "licensing state is ambiguous", "")]
    [InlineData("missing-addon-flag", "licensing state is ambiguous", "")]
    [InlineData("missing-status", "licensing state is ambiguous", "")]
    [InlineData("missing-id", "licensing state is ambiguous", "")]
    [InlineData("refresh-error", "stage=refresh", "InstallProductKey,RefreshLicenseStatus")]
    [InlineData("discovery-error", "stage=discovery", "")]
    [InlineData("install-error", "stage=install", "InstallProductKey")]
    [InlineData("install-rejected", "stage=install; HRESULT=0xC004F050", "InstallProductKey")]
    [InlineData("missing-installed-product", "installed firmware key could not be verified", "InstallProductKey,RefreshLicenseStatus")]
    [InlineData("activated-after-install", "Windows OEM activation succeeded.", "InstallProductKey,RefreshLicenseStatus")]
    [InlineData("activation-error", "stage=activate", "InstallProductKey,RefreshLicenseStatus,Activate")]
    [InlineData("offline", "stage=activate; HRESULT=0xC004F074", "InstallProductKey,RefreshLicenseStatus,Activate")]
    [InlineData("pending", "Windows remains unactivated", "InstallProductKey,RefreshLicenseStatus,Activate,RefreshLicenseStatus")]
    public async Task Activation_PreservesLicensingIntentAndReportsSanitizedOutcome(string scenario, string outcome, string methods)
    {
        using var workspace = new TemporaryDirectory();
        string activationPath = Path.Combine(workspace.Path, "Activate-WindowsOem.ps1");
        using Stream? resource = typeof(PreOobeScriptDefinitionBuilder).Assembly.GetManifestResourceStream("Foundry.Deploy.PreOobe.Activate-WindowsOem.ps1");
        Assert.NotNull(resource);
        using var reader = new StreamReader(resource);
        await File.WriteAllTextAsync(activationPath, await reader.ReadToEndAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        string harness = $"$Scenario = '{scenario}'\n$ActivationPath = '{activationPath.Replace("'", "''", StringComparison.Ordinal)}'\n" + Harness;
        string harnessPath = Path.Combine(workspace.Path, "Test-Activation.ps1");
        await File.WriteAllTextAsync(harnessPath, harness, TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        ProcessExecutionResult result = await new ProcessRunner().RunAsync(new ProcessExecutionRequest(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", harnessPath], workspace.Path), timeout.Token);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Contains(outcome, result.StandardOutput);
        Assert.Contains($"METHODS=[{methods}]", result.StandardOutput);
        Assert.DoesNotContain("BBBBB-CCCCC-DDDDD-FFFFF-GGGGG", result.StandardOutput);
        Assert.DoesNotContain("PRIVATE ERROR", result.StandardOutput);
        Assert.DoesNotContain("INVALID MOCK CALL", result.StandardOutput);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry-OemActivation-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private const string Harness = """
        $ErrorActionPreference = 'Stop'
        $global:Methods = New-Object 'System.Collections.Generic.List[string]'
        $global:Violations = New-Object 'System.Collections.Generic.List[string]'
        $global:Installed = $false
        $global:Activated = $false
        $global:FirmwareKey = 'BBBBB-CCCCC-DDDDD-FFFFF-GGGGG'
        $global:Edition = switch ($Scenario) {
            'home' { 'Core' }
            'home-n' { 'CoreN' }
            'home-single-language' { 'CoreSingleLanguage' }
            'home-country-specific' { 'CoreCountrySpecific' }
            'pro-n' { 'ProfessionalN' }
            'unknown-edition' { 'Enterprise' }
            default { 'Professional' }
        }
        $global:DefaultSuffix = switch ($Scenario) {
            'home' { '8HVX7' }
            'home-n' { 'WXCHW' }
            'home-single-language' { '6F4BT' }
            'home-country-specific' { '8TYMD' }
            'pro-n' { 'PKCKT' }
            default { '3V66T' }
        }
        function Get-ItemProperty {
            param($LiteralPath, $Path, $Name)
            if (($LiteralPath + $Path) -notlike '*Windows NT\CurrentVersion') { throw 'INVALID MOCK CALL: registry' }
            [pscustomobject]@{ EditionID = $global:Edition }
        }
        function Get-CimInstance {
            param($ClassName, $Filter, $OperationTimeoutSec, $ErrorAction)
            if ($OperationTimeoutSec -le 0 -or $OperationTimeoutSec -gt 15) { $global:Violations.Add('CIM timeout') }
            switch ($ClassName) {
                'Win32_OperatingSystem' { [pscustomobject]@{ ProductType = $(if ($Scenario -eq 'server') { 3 } else { 1 }) }; return }
                'SoftwareLicensingService' {
                    [pscustomobject]@{
                        OA3xOriginalProductKey = $(if ($Scenario -eq 'no-key') { '' } elseif ($Scenario -eq 'invalid-key') { 'invalid' } else { $global:FirmwareKey })
                        OA3xOriginalProductKeyDescription = $(if ($Scenario -eq 'incompatible') { 'Win 10 RTM Core OEM:DM' } elseif ($Scenario -eq 'unknown-description') { '' } elseif ($Scenario -eq 'similar-edition') { 'Win 10 RTM ProfessionalN OEM:DM' } else { "Win 10 RTM $global:Edition OEM:DM" })
                        KeyManagementServiceMachine = $(if ($Scenario -eq 'configured-kms') { 'kms.invalid' } else { '' })
                        IsKeyManagementServiceMachine = 0
                    }
                    return
                }
                'SoftwareLicensingProduct' {
                    if ($Filter -notlike '*55c92734-d682-4d71-983e-d6ec3f16059f*') { $global:Violations.Add('Windows filter') }
                    if ($Scenario -eq 'discovery-error') { throw "PRIVATE ERROR $global:FirmwareKey" }
                    if ($Scenario -eq 'missing-installed-product' -and $global:Installed) { return }
                    $product = [pscustomobject]@{
                        ID = $(if ($global:Installed -or $Scenario -eq 'already-oem') { 'firmware-product' } else { 'default-product' })
                        ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f'
                        LicenseFamily = $global:Edition
                        LicenseIsAddon = $false
                        LicenseStatus = $(if ($Scenario -eq 'activated' -or ($global:Activated -and $Scenario -ne 'pending') -or ($global:Installed -and $Scenario -eq 'activated-after-install')) { 1 } else { 0 })
                        PartialProductKey = $(if ($global:Installed -or $Scenario -eq 'already-oem') { 'GGGGG' } elseif ($Scenario -eq 'custom-retail') { 'XXXXX' } else { $global:DefaultSuffix })
                        ProductKeyChannel = $(if ($global:Installed -or $Scenario -eq 'already-oem') { 'OEM:DM' } elseif ($Scenario -eq 'volume') { 'Volume:GVLK' } elseif ($Scenario -eq 'mak') { 'Volume:MAK' } else { 'Retail' })
                        Description = $(if ($Scenario -in @('volume', 'mak')) { 'Windows(R) Operating System, VOLUME channel' } else { 'Windows(R) Operating System, RETAIL channel' })
                        KeyManagementServiceMachine = ''
                    }
                    if ($Scenario -eq 'missing-addon-flag') { $product.LicenseIsAddon = $null }
                    if ($Scenario -eq 'missing-status') { $product.LicenseStatus = $null }
                    if ($Scenario -eq 'missing-id') { $product.ID = '' }
                    $product
                    if ($Scenario -eq 'ambiguous') { $product.PSObject.Copy() }
                    if ($Scenario -eq 'addon') { $addon = $product.PSObject.Copy(); $addon.ID = 'addon'; $addon.LicenseIsAddon = $true; $addon.LicenseStatus = 1; $addon }
                    return
                }
                default { $global:Violations.Add('Unexpected CIM class'); throw 'INVALID MOCK CALL' }
            }
        }
        function Invoke-CimMethod {
            param($InputObject, $MethodName, $Arguments, $OperationTimeoutSec, $ErrorAction)
            if ($OperationTimeoutSec -le 0 -or $OperationTimeoutSec -gt 15) { $global:Violations.Add('CIM timeout') }
            $global:Methods.Add($MethodName)
            switch ($MethodName) {
                'InstallProductKey' {
                    if ($Arguments.ProductKey -ne $global:FirmwareKey) { $global:Violations.Add('Wrong product key') }
                    if ($Scenario -eq 'install-error') { throw "PRIVATE ERROR $global:FirmwareKey" }
                    if ($Scenario -eq 'install-rejected') { return [pscustomobject]@{ ReturnValue = [uint32]3221549136 } }
                    $global:Installed = $true
                }
                'Activate' {
                    if ($InputObject.ID -ne 'firmware-product') { $global:Violations.Add('Wrong activation target') }
                    if ($Scenario -eq 'activation-error') { throw "PRIVATE ERROR $global:FirmwareKey" }
                    if ($Scenario -eq 'offline') { return [pscustomobject]@{ ReturnValue = [uint32]3221549172 } }
                    $global:Activated = $true
                }
                'RefreshLicenseStatus' { if ($Scenario -eq 'refresh-error') { throw "PRIVATE ERROR $global:FirmwareKey" } }
                default { $global:Violations.Add('Unexpected CIM method'); throw 'INVALID MOCK CALL' }
            }
            [pscustomobject]@{ ReturnValue = [uint32]0 }
        }
        & $ActivationPath
        if ($global:Violations.Count -gt 0) { throw ('INVALID MOCK CALL: ' + ($global:Violations -join ', ')) }
        Write-Output ('METHODS=[' + ($global:Methods -join ',') + ']')
        """;
}
