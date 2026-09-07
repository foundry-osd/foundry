// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeScriptBehaviorTests
{
    [Fact]
    public async Task Plan_CleanupOnlyDeniedDeleteThrowsAndPersistsFailure()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            $secret=Join-Path $root 'Data\secret.txt'
            [IO.File]::WriteAllText($secret,'fixture secret')
            $action=@{id='one';fileName='one.ps1';dataFiles=@(@{fileName='secret.txt';owningActionId='one';cleanupDisposition='SecretAlways';isSensitive=$true})}
            $manifest=Join-Path $root 'manifest.json'; $results=Join-Path $root 'results.json'
            [IO.File]::WriteAllText($manifest,(@{version=1;scripts=@($action)}|ConvertTo-Json -Depth 8))
            Write-FoundryResults $results @([pscustomobject]@{id='one';status='staged';attempt=0;errorCode=$null})
            function Invoke-FoundryInstaller { throw 'Native boundary must not run.' }
            function Remove-Item { param($LiteralPath,[switch]$Force); throw [UnauthorizedAccessException]::new('fixture denied deletion') }
            $failed=$false
            try { $null=Invoke-FoundryPlan $manifest $results -CleanupSecretsOnly }
            catch { $failed=$_.Exception.Message -eq 'input_cleanup_failed' }
            if(-not $failed) { throw 'Denied cleanup returned success.' }
            $journal=@(Read-FoundryResults $results)
            if($journal[0].status -ne 'failed' -or $journal[0].errorCode -ne 'input_cleanup_failed' -or $journal[0].attempt -ne 0) { throw 'Cleanup failure evidence lost.' }
            if(-not [IO.File]::Exists($secret)) { throw 'Fixture did not deny deletion.' }
            """);
    }

    [Fact]
    public async Task Plan_CleanupOnlyErasesSecretsWithoutStartingActions()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $root 'Data\secret.txt'),'secret')
            $action=@{id='one';fileName='one.ps1';dataFiles=@(@{fileName='secret.txt';owningActionId='one';cleanupDisposition='SecretAlways';isSensitive=$true})}
            $manifest=Join-Path $root 'manifest.json'; $results=Join-Path $root 'results.json'
            [IO.File]::WriteAllText($manifest,(@{version=1;scripts=@($action)}|ConvertTo-Json -Depth 8))
            Write-FoundryResults $results @([pscustomobject]@{id='one';status='staged';attempt=0;errorCode=$null})
            function Invoke-FoundryInstaller { throw 'Native boundary must not run.' }
            $null=Invoke-FoundryPlan $manifest $results -CleanupSecretsOnly
            if([IO.File]::Exists((Join-Path $root 'Data\secret.txt'))) { throw 'Unstarted secret retained.' }
            if((@(Read-FoundryResults $results))[0].status -ne 'staged') { throw 'Cleanup was reported as action success.' }
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_RetrySuccessAndCleanupFailureAreJournaled(bool cleanupFailure)
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            [IO.Directory]::CreateDirectory((Join-Path $root 'Scripts')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $root 'Scripts\one.ps1'),'fixture')
            [IO.File]::WriteAllText((Join-Path $root 'Data\input.bin'),'fixture')
            $action=[pscustomobject]@{id='one';fileName='one.ps1';arguments=@();dataFiles=@(@{fileName='input.bin';owningActionId='one';cleanupDisposition='RetainForRetry';isSensitive=$false})}
            $results=Join-Path $root 'results.json'
            Write-FoundryResults $results @([pscustomobject]@{id='one';status='failed';attempt=1;errorCode='native_exit_failed'})
            function Invoke-FoundryInstaller { param($FilePath,$Arguments,$TimeoutSeconds); return 0 }
            if($env:FOUNDRY_TEST_CLEANUP_FAIL -eq 'true') { function Remove-Item { param($LiteralPath,[switch]$Force); throw 'fixture cleanup failure' } }
            $result=Invoke-FoundryAction $action 2 $results
            if($env:FOUNDRY_TEST_CLEANUP_FAIL -eq 'true') {
                if($result.status -ne 'failed' -or $result.errorCode -ne 'input_cleanup_failed' -or -not [IO.File]::Exists((Join-Path $root 'Data\input.bin'))) { throw 'Cleanup failure was hidden.' }
            }
            elseif($result.status -ne 'succeeded' -or [IO.File]::Exists((Join-Path $root 'Data\input.bin'))) { throw 'Successful retry did not complete.' }
            if((@(Read-FoundryResults $results))[0].attempt -ne 2) { throw 'Retry attempt lost.' }
            """, new() { ["FOUNDRY_TEST_CLEANUP_FAIL"] = cleanupFailure ? "true" : "false" });
    }

    [Theory]
    [InlineData(1618, false)]
    [InlineData(3010, true)]
    public async Task DriverScript_PreservesActualFailureAndEarlierReboot(int initialCode, bool reboot)
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            $package=Join-Path $root 'package.exe'
            [IO.File]::WriteAllText($package,'fixture')
            $hash=[Security.Cryptography.SHA256]::Create()
            try { $digest=[BitConverter]::ToString($hash.ComputeHash([IO.File]::ReadAllBytes($package))).Replace('-','') } finally { $hash.Dispose() }
            $script:calls=0; $registryState=@{Restored=$false}
            function Invoke-FoundryInstaller { param($FilePath,$Arguments,$TimeoutSeconds); $script:calls++; if($script:calls -eq 1) { return [int]$env:FOUNDRY_TEST_CODE }; return 1618 }
            function Start-Job { param($ScriptBlock,$ArgumentList); [pscustomobject]@{State='Completed'} }
            function Wait-Job { param($Job,$Timeout); return $Job }
            function Receive-Job { param($Job); [pscustomobject]@{Status='Valid';Subject='CN=Lenovo, OU=G10, O=Lenovo, L=Morrisville, S=North Carolina, C=US'} }
            function Stop-Job { param($Job) }
            function Remove-Job { param($Job,[switch]$Force) }
            function Get-FoundryDriverPath { return [pscustomobject]@{Exists=$true;Value='%prior%';Kind='ExpandString'} }
            function Set-FoundryDriverPath { param($State); if($State.Value -eq '%prior%' -and $State.Kind -eq 'ExpandString') { $registryState.Restored=$true } }
            & (Join-Path $root 'driver.ps1') -CommandKind LenovoExecutable -PackagePath $package -ExpectedSha256 $digest -ExpectedSizeBytes 7
            if($LASTEXITCODE -ne 1618) { throw 'Actual native failure was lost.' }
            $outcome=@(Read-FoundryResults (Join-Path $root 'driver-outcome.json'))
            if($outcome[0].exitCode -ne 1618 -or $outcome[0].rebootRequired -ne ($env:FOUNDRY_TEST_REBOOT -eq 'true')) { throw 'Native metadata lost.' }
            if(-not [IO.File]::Exists($package)) { throw 'Failed driver package deleted.' }
            if($env:FOUNDRY_TEST_REBOOT -eq 'true' -and -not $registryState.Restored) { throw 'Prior registry value not restored.' }
            """, new() { ["FOUNDRY_TEST_CODE"] = initialCode.ToString(), ["FOUNDRY_TEST_REBOOT"] = reboot ? "true" : "false" });
    }

    [Fact]
    public async Task NetworkFunctions_PreserveWhitespaceAndFailRequiredImports()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $tokens=$null; $parseErrors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $env:FOUNDRY_TEST_ROOT 'network.ps1'),[ref]$tokens,[ref]$parseErrors)
            foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) {
                . ([ScriptBlock]::Create($function.Extent.Text))
            }
            $DataDirectory=$env:FOUNDRY_TEST_ROOT
            $profile=Join-Path $DataDirectory 'wifi.xml'
            [IO.File]::WriteAllText($profile,'<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>   </name><SSIDConfig><SSID><name> </name></SSID></SSIDConfig><connectionType>ESS</connectionType><MSM><security><sharedKey><keyMaterial>        </keyMaterial></sharedKey></security></MSM></WLANProfile>')
            Set-FoundryWifiProfileConnectionMode $profile 'preOobeConnectable'
            if((Get-FoundryWifiProfileName $profile) -cne '   ') { throw 'Profile name altered.' }
            $document=[Xml.XmlDocument]::new(); $document.PreserveWhitespace=$true; $document.Load($profile)
            if($document.SelectSingleNode('//*[local-name()="keyMaterial"]').InnerText -cne '        ') { throw 'Password altered.' }
            function Invoke-FoundryInstaller { param($FilePath,$Arguments,$TimeoutSeconds); return 5 }
            $failed=$false
            try { Invoke-FoundryNetsh @('wlan','add','profile') } catch { $failed=$_.Exception.Message -eq 'network_profile_import_failed' }
            if(-not $failed) { throw 'Required network failure ignored.' }
            [IO.File]::WriteAllText((Join-Path $DataDirectory 'cert.cer'),'fixture')
            $failed=$false
            try { Import-FoundryCertificate ([pscustomobject]@{relativePath='cert.cer';kind='cer';storeName='Root'}) } catch { $failed=$_.Exception.Message -eq 'certificate_import_failed' }
            if(-not $failed) { throw 'Required certificate failure ignored.' }
            function Import-PfxCertificate { param($FilePath,$CertStoreLocation,$Exportable); throw 'fixture certificate failure' }
            $failed=$false
            try { Import-FoundryCertificate ([pscustomobject]@{relativePath='cert.cer';kind='pfx';passwordRelativePath=$null}) } catch { $failed=$_.Exception.Message -eq 'certificate_import_failed' }
            if(-not $failed) { throw 'Required PFX failure ignored.' }
            """);
    }

    [Fact]
    public async Task Plan_ContinuesIndependentActionsAndBoundsExplicitRetry()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            [IO.Directory]::CreateDirectory((Join-Path $root 'Scripts')) | Out-Null
            $actions=foreach($id in @('first','second','dependent')) {
                [IO.File]::WriteAllText((Join-Path $root "Scripts\$id.ps1"),'fixture')
                [pscustomobject]@{id=$id;fileName="$id.ps1";arguments=@();dependsOn=@();dataFiles=@()}
            }
            $actions[2].dependsOn=@('first')
            $manifest=Join-Path $root 'manifest.json'; $results=Join-Path $root 'results.json'
            [IO.File]::WriteAllText($manifest,(@{version=1;scripts=$actions}|ConvertTo-Json -Depth 8))
            $script:calls=0
            function Invoke-FoundryInstaller { param($FilePath,$Arguments,$TimeoutSeconds); $script:calls++; if($Arguments -match 'first.ps1') { return 1618 }; return 0 }
            $run=@(Invoke-FoundryPlan $manifest $results)
            if($script:calls -ne 2 -or ($run|Where-Object id -eq 'second').status -ne 'succeeded' -or ($run|Where-Object id -eq 'dependent').status -ne 'skipped_dependency') { throw ('Independent continuation failed: ' + $script:calls + ' ' + ($run|ConvertTo-Json -Depth 8)) }
            $null=Invoke-FoundryPlan $manifest $results
            if($script:calls -ne 2) { throw 'Automatic retry occurred.' }
            $null=Invoke-FoundryPlan $manifest $results -RetryFailed -ActionId first
            $null=Invoke-FoundryPlan $manifest $results -RetryFailed -ActionId first
            $null=Invoke-FoundryPlan $manifest $results -RetryFailed -ActionId first
            if($script:calls -ne 4) { throw 'Attempt limit not enforced.' }
            $null=Invoke-FoundryPlan $manifest $results -RetryFailed -ActionId second
            if($script:calls -ne 4) { throw 'Succeeded action reran.' }
            """);
    }

    [Fact]
    public async Task Plan_StaleRunningRequiresRecoveryAndCleansUnstartedSecrets()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $root 'Data\secret.txt'),'secret')
            $action=[pscustomobject]@{id='one';fileName='one.ps1';arguments=@();dependsOn=@();dataFiles=@(@{fileName='secret.txt';owningActionId='one';cleanupDisposition='SecretAlways';isSensitive=$true})}
            $manifest=Join-Path $root 'manifest.json'; $results=Join-Path $root 'results.json'
            [IO.File]::WriteAllText($manifest,(@{version=1;scripts=@($action)}|ConvertTo-Json -Depth 8))
            Write-FoundryResults $results @([pscustomobject]@{id='one';status='running';attempt=1;errorCode=$null})
            function Invoke-FoundryInstaller { throw 'Native boundary must not run.' }
            $blocked=$false
            try { $null=Invoke-FoundryPlan $manifest $results -RetryFailed } catch { Write-Output $_.Exception.Message; $blocked=$_.Exception.Message -eq 'native_recovery_required' }
            if(-not $blocked) { throw 'Stale running state authorized retry.' }
            if([IO.File]::Exists((Join-Path $root 'Data\secret.txt'))) { throw 'Unstarted secret retained.' }
            """);
    }

    [Fact]
    public async Task Action_RetryRequiresErasedSecretToBeReentered()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root=$env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            $action=[pscustomobject]@{id='one';fileName='one.ps1';arguments=@();dependsOn=@();dataFiles=@(@{fileName='secret.txt';owningActionId='one';cleanupDisposition='SecretAlways';isSensitive=$true})}
            function Invoke-FoundryInstaller { throw 'Native boundary must not run.' }
            $result=Invoke-FoundryAction $action 1 (Join-Path $root 'results.json')
            if($result.errorCode -ne 'secret_reentry_required' -or $result.status -ne 'failed') { throw 'Missing secret was not recorded.' }
            """);
    }

    [Fact]
    public async Task NativeTimeout_PersistsOwnershipWithoutClaimingTermination()
    {
        await RunHarnessAsync("""
            $ErrorActionPreference='Stop'
            . $env:FOUNDRY_TEST_FUNCTIONS
            $script:waited=0
            function Start-Process {
                param($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru)
                if($WindowStyle -ne 'Hidden' -or -not $PassThru) { throw 'Invalid process options.' }
                $process=[pscustomobject]@{Id=1234;StartTime=[DateTime]::UtcNow}
                $process|Add-Member ScriptMethod WaitForExit { param($milliseconds); $script:waited=$milliseconds; return $false }
                $process|Add-Member ScriptMethod Dispose { }
                return $process
            }
            $blocked=$false
            try { Invoke-FoundryInstaller 'fixture.exe' @('argument') } catch { $blocked=$_.Exception.Data['OwnershipUncertain'] -eq $true }
            if(-not $blocked -or $script:waited -ne 1800000) { throw 'Timeout ownership lost.' }
            $state=@(Read-FoundryResults (Join-Path $env:FOUNDRY_TEST_ROOT 'native-uncertainty.json'))
            if($state[0].processId -ne 1234) { throw 'Native identity not persisted.' }
            """);
    }

    [Theory]
    [InlineData(1618, "failed", false)]
    [InlineData(3010, "succeeded", true)]
    public async Task Action_RecordsNativeOutcomeAndAlwaysDeletesSecrets(int code, string status, bool reboot)
    {
        string harness = """
            . $env:FOUNDRY_TEST_FUNCTIONS
            $root = $env:FOUNDRY_TEST_ROOT
            [IO.Directory]::CreateDirectory((Join-Path $root 'Data')) | Out-Null
            [IO.Directory]::CreateDirectory((Join-Path $root 'Scripts')) | Out-Null
            [IO.File]::WriteAllText((Join-Path $root 'Scripts\driver.ps1'), 'harmless fixture')
            [IO.File]::WriteAllText((Join-Path $root 'Data\secret.txt'), 'secret')
            [IO.File]::WriteAllText((Join-Path $root 'Data\package.bin'), 'retry input')
            function Invoke-FoundryInstaller { param($FilePath, $Arguments, $TimeoutSeconds); return [int]$env:FOUNDRY_TEST_CODE }
            $action = [pscustomobject]@{ id='driver-pack'; fileName='driver.ps1'; arguments=@(); dependsOn=@(); dataFiles=@(
                [pscustomobject]@{fileName='secret.txt'; owningActionId='driver-pack'; cleanupDisposition='SecretAlways'; isSensitive=$true},
                [pscustomobject]@{fileName='package.bin'; owningActionId='driver-pack'; cleanupDisposition='RetainForRetry'; isSensitive=$false}) }
            $result = Invoke-FoundryAction -Action $action -Attempt 1 -ResultsPath (Join-Path $root 'results.json')
            if ($result.status -ne $env:FOUNDRY_TEST_STATUS) { throw 'Wrong result state.' }
            if ($result.exitCode -ne [int]$env:FOUNDRY_TEST_CODE) { throw 'Wrong exit code.' }
            if ($result.rebootRequired -ne ($env:FOUNDRY_TEST_REBOOT -eq 'true')) { throw 'Lost reboot state.' }
            if ([IO.File]::Exists((Join-Path $root 'Data\secret.txt'))) { throw 'Secret retained.' }
            if ($result.status -eq 'failed' -and -not [IO.File]::Exists((Join-Path $root 'Data\package.bin'))) { throw 'Retry input deleted.' }
            $journal = @(Get-Content -LiteralPath (Join-Path $root 'results.json') -Raw | ConvertFrom-Json)
            if ($journal[0].status -ne $result.status) { throw 'Terminal result not published.' }
            """;
        await RunHarnessAsync(harness, new()
        {
            ["FOUNDRY_TEST_CODE"] = code.ToString(),
            ["FOUNDRY_TEST_STATUS"] = status,
            ["FOUNDRY_TEST_REBOOT"] = reboot ? "true" : "false"
        });
    }

    private static async Task RunHarnessAsync(string harness, Dictionary<string, string?>? environment = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryFirstBootTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string functions = Path.Combine(root, "functions.ps1");
            using Stream resource = typeof(PreOobeScriptResources).Assembly.GetManifestResourceStream(PreOobeScriptResources.Functions)!;
            using (FileStream output = File.Create(functions)) resource.CopyTo(output);
            foreach ((string name, string target) in new[] { (PreOobeScriptResources.InstallDriverPack, "driver.ps1"), (PreOobeScriptResources.ImportNetworkProfiles, "network.ps1") })
            {
                using Stream script = typeof(PreOobeScriptResources).Assembly.GetManifestResourceStream(name)!;
                using FileStream output = File.Create(Path.Combine(root, target));
                script.CopyTo(output);
            }
            environment ??= new();
            environment["FOUNDRY_TEST_ROOT"] = root;
            environment["FOUNDRY_TEST_FUNCTIONS"] = functions;
            var request = new ProcessExecutionRequest(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(harness))], root)
            { ExecutionTimeout = TimeSpan.FromSeconds(30), EnvironmentOverrides = environment };
            ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess, result.ToDiagnosticText());
        }
        finally { Directory.Delete(root, true); }
    }
}
