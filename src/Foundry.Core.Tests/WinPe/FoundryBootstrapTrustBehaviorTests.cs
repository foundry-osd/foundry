// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class FoundryBootstrapTrustBehaviorTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("reparse")]
    public async Task AuthenticatedArchive_RejectsUnsafeEntriesBeforeExtraction(string scenario)
    {
        await RunAsync("""
            Add-Type -AssemblyName System.IO.Compression
            $path=Join-Path $root 'runtime.zip'
            $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew)
            $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
            try {
                $entry=$zip.CreateEntry('Foundry.Connect.exe');$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('fixture')}finally{$writer.Dispose()}
                $name=switch($env:FOUNDRY_SCENARIO){traversal {'../escape.dll'};duplicate {'FOUNDRY.CONNECT.EXE'};default {'library.dll'}}
                $entry=$zip.CreateEntry($name)
                if($env:FOUNDRY_SCENARIO -eq 'reparse'){$entry.ExternalAttributes=1024}
                $writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('fixture')}finally{$writer.Dispose()}
            } finally {$zip.Dispose();$stream.Dispose()}
            $destination=Join-Path $root 'extracted';$failed=$false
            try{$files=Expand-AuthenticatedRuntimeArchive $path (Get-FileSha256 $path) $destination 'Foundry.Connect'}catch{$failed=$true}
            if($failed -ne ($env:FOUNDRY_SCENARIO -ne 'valid')){throw 'Archive path policy incorrect.'}
            if($env:FOUNDRY_SCENARIO -eq 'valid') {
                if(@($files).Count -ne 2 -or -not (Test-RuntimeFiles $destination $files)){throw 'Authenticated tree was not recorded.'}
            } elseif([IO.Directory]::Exists($destination) -or [IO.File]::Exists((Join-Path $root 'escape.dll'))){throw 'Unsafe archive wrote before validation.'}
            """, scenario);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("digest")]
    [InlineData("missing-digest")]
    [InlineData("length")]
    [InlineData("header-timeout")]
    public async Task Download_UsesBoundedFakeTransportAndPublishesOnlyVerifiedBytes(string scenario)
    {
        await RunAsync("""
            Add-Type -TypeDefinition 'public sealed class TimedFixtureStream : System.IO.MemoryStream { public TimedFixtureStream(byte[] bytes):base(bytes){} public override bool CanTimeout {get{return true;}} public override int ReadTimeout {get;set;} }'
            $payload=Join-Path $root 'source.bin';[IO.File]::WriteAllText($payload,'verified bytes')
            $digest=Get-FileSha256 $payload;$length=([IO.FileInfo]$payload).Length
            $destination=Join-Path $root 'runtime.zip';[IO.File]::WriteAllText($destination,'previous')
            $script:transfer=@{calls=0;aborted=$false;disposed=$false;bytes=[IO.File]::ReadAllBytes($payload)}
            function New-BootstrapWebRequest {
                param($uri)
                $script:transfer.calls++
                $request=[pscustomobject]@{AllowAutoRedirect=$true;Timeout=0;ReadWriteTimeout=0;UserAgent=''}
                $request|Add-Member ScriptMethod Abort {$script:transfer.aborted=$true}
                $request|Add-Member ScriptMethod BeginGetResponse {
                    param($callback,$state)
                    if($this.AllowAutoRedirect -or $this.Timeout -gt 1000 -or $this.ReadWriteTimeout -gt 1000){throw 'Unbounded request.'}
                    $handle=[pscustomobject]@{}
                    $handle|Add-Member ScriptMethod WaitOne {param($timeout);return $env:FOUNDRY_SCENARIO -ne 'header-timeout'}
                    return @{AsyncWaitHandle=$handle;IsCompleted=$false}
                }
                $request|Add-Member ScriptMethod EndGetResponse {
                    param($pending)
                    $response=[pscustomobject]@{StatusCode=200;ContentLength=$script:transfer.bytes.Length;Headers=@{}}
                    $response|Add-Member ScriptMethod GetResponseStream {return [TimedFixtureStream]::new($script:transfer.bytes)}
                    $response|Add-Member ScriptMethod Dispose {$script:transfer.disposed=$true}
                    return $response
                }
                return $request
            }
            switch($env:FOUNDRY_SCENARIO){digest{$digest='0'*64};missing-digest{$digest=''};length{$length++}}
            $failed=$false
            try{$null=Save-WebFile 'https://fixture.invalid/runtime.zip' $destination $digest $length -OverallTimeoutSeconds 1}catch{$failed=$true}
            if($failed -ne ($env:FOUNDRY_SCENARIO -ne 'valid')){throw 'Download verification decision incorrect.'}
            $expected=if($env:FOUNDRY_SCENARIO -eq 'valid'){'verified bytes'}else{'previous'}
            if([IO.File]::ReadAllText($destination) -ne $expected -or @([IO.Directory]::GetFiles($root,'*.partial')).Count){throw 'Partial download published or retained.'}
            if($env:FOUNDRY_SCENARIO -eq 'missing-digest') {if($script:transfer.calls -ne 0){throw 'Missing identity used HTTP.'}}
            elseif(-not $script:transfer.aborted){throw 'Transport was not aborted/disposed.'}
            """, scenario);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("debug-valid")]
    [InlineData("debug-changed")]
    public async Task ArchiveOverrides_CannotChangeAuthoredTrust(string scenario)
    {
        await RunAsync("""
            $fallbackRoot=Join-Path $root 'fallback';[IO.Directory]::CreateDirectory($fallbackRoot)|Out-Null
            $exe=Join-Path $fallbackRoot 'Foundry.Connect.exe';[IO.File]::WriteAllText($exe,'pinned')
            $files=@(@{relativePath='Foundry.Connect.exe';length=6;sha256=(Get-FileSha256 $exe)})
            $kind=if($env:FOUNDRY_SCENARIO -eq 'release'){'Release'}else{'Debug'}
            $script:TrustedMediaManifest=@{applications=@(@{applicationName='Foundry.Connect';source=$kind;files=$files})}
            $script:ExecutionIdentities=@{}
            $archive=Join-Path $root 'override.zip';[IO.File]::WriteAllText($archive,'fake archive')
            function Get-ArchiveOverridePath {param($application);if($env:FOUNDRY_SCENARIO -eq 'release'){throw 'Release override must not be read.'};return $archive}
            function Get-ArchiveOverrideSha256 {return ''}
            function Invoke-WithRetry {throw 'Fake release source unavailable.'}
            function Write-Log {}
            function Expand-AuthenticatedRuntimeArchive {
                param($archivePath,$digest,$destination,$application)
                [IO.Directory]::CreateDirectory($destination)|Out-Null
                $bytes=if($env:FOUNDRY_SCENARIO -eq 'debug-valid'){'pinned'}else{'changed'}
                [IO.File]::WriteAllText((Join-Path $destination 'Foundry.Connect.exe'),$bytes)
                return @()
            }
            $actual=Resolve-AuthenticatedRuntime 'Foundry.Connect' 'win-x64' $root @{} ([IO.FileInfo]$exe)
            if($env:FOUNDRY_SCENARIO -eq 'debug-valid') {
                if($actual.FullName -eq $exe -or -not (Test-RuntimeFiles $actual.DirectoryName $files)){throw 'Matching authored override rejected.'}
            } elseif($actual.FullName -ne $exe){throw 'Untrusted override replaced authored runtime.'}
            """, scenario);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("abort")]
    [InlineData("tampered")]
    public async Task OfflineMainFlow_DoesNotFetchOrTreatAbortAsSuccess(string scenario)
    {
        await RunAsync("""
            $WinPeRoot=$root;$DiagnosticSessionId='fixture'
            $EmbeddedDeployConfigurationPath=Join-Path $root 'deploy.json';[IO.File]::WriteAllText($EmbeddedDeployConfigurationPath,'{}')
            $EmbeddedConnectConfigurationPath=Join-Path $root 'connect.json'
            $exe=Join-Path $root 'Foundry.Deploy.exe';[IO.File]::WriteAllText($exe,'fixture')
            $script:started=0
            $media=@{mediaId=[Guid]::NewGuid().ToString('D');runtimeIdentifier='win-x64';target='Iso';catalogSnapshots=@(@{id='operating-systems';revision='pinned'})}
            function Write-Log {};function Write-ConsoleBanner {};function Ensure-ServiceRunning {return $true};function Start-WinPeWirelessServiceIfSupported {};function Copy-BootstrapLogsToCache {}
            function Get-TargetRuntimeIdentifier {return 'win-x64'}
            function Read-TrustedMediaManifest {return $media}
            function Get-UsbCacheRuntimeRoot {return $null}
            function Resolve-PinnedRuntime {return [IO.FileInfo]$exe}
            function Invoke-OfflineReadinessChild {
                param($executable,$configurationPath,$resultPath,$nonce)
                $envelope=@{Version=1;Nonce=$nonce;CanBrowse=$true;RuntimeIdentifier='win-x64';Result=@{CanContinue=$false;MediaId=$media.mediaId;ConfigurationDigest=(Get-FileSha256 $configurationPath);CatalogRevisions=@{'operating-systems'='pinned'};BlockingReasons=@('SelectionIncomplete')}}
                [IO.File]::WriteAllText($resultPath,($envelope|ConvertTo-Json -Depth 8));return 2
            }
            function Invoke-ConnectExecutable {
                param($Executable,$ConfigurationPath,$OfflineReadinessPath,$OfflineNonce)
                if($env:FOUNDRY_SCENARIO -eq 'abort'){return 20}
                if($env:FOUNDRY_SCENARIO -eq 'tampered'){[IO.File]::AppendAllText($OfflineReadinessPath,' ')}
                return 23
            }
            function Start-DeployExecutable {param($Executable,[switch]$Offline);if(-not $Offline){throw 'Offline flag missing.'};$script:started++}
            function Resolve-AuthenticatedRuntime {throw 'Offline acquisition must not run.'}
            function Sync-WinPeInternetDateTime {throw 'Offline time network must not run.'}
            function Set-WinPeTimeZone {throw 'Offline timezone network must not run.'}
            $execution=$source.Substring($source.IndexOf('#region Bootstrap Execution'))
            $execution=$execution.Replace('if ($script:BootstrapExitCode -eq 1) { exit 1 }','')
            . ([scriptblock]::Create($execution))
            if($script:started -ne $(if($env:FOUNDRY_SCENARIO -eq 'offline'){1}else{0})){throw 'Offline/abort launch decision incorrect.'}
            if($env:FOUNDRY_SCENARIO -eq 'offline' -and $script:BootstrapExitCode -eq 1){throw 'Valid offline browsing failed.'}
            """, scenario);
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("browse")]
    [InlineData("invalid")]
    [InlineData("contradiction")]
    public async Task ReadinessHandoff_UsesOwnedNonceAndPreservesExitMeaning(string scenario)
    {
        await RunAsync("""
            $config=Join-Path $root 'config.json';[IO.File]::WriteAllText($config,'{}')
            $exe=Join-Path $root 'Foundry.Deploy.exe';[IO.File]::WriteAllText($exe,'fixture')
            $manifest=@{mediaId=[Guid]::NewGuid().ToString('D');runtimeIdentifier='win-x64';catalogSnapshots=@(@{id='operating-systems';revision='pinned'})}
            function Invoke-OfflineReadinessChild {
                param($executable,$configurationPath,$resultPath,$nonce)
                if(-not $resultPath.StartsWith($root) -or [Guid]::Parse($nonce) -eq [Guid]::Empty){throw 'Unowned handoff.'}
                $result=@{Version=1;Nonce=$nonce;CanBrowse=$true;RuntimeIdentifier='win-x64';Result=@{CanContinue=($env:FOUNDRY_SCENARIO -eq 'ready');MediaId=$manifest.mediaId;ConfigurationDigest=(Get-FileSha256 $configurationPath);CatalogRevisions=@{'operating-systems'='pinned'};BlockingReasons=@()}}
                [IO.File]::WriteAllText($resultPath,($result|ConvertTo-Json -Depth 8))
                switch($env:FOUNDRY_SCENARIO){ready{return 0};browse{return 2};invalid{return 1};contradiction{return 0}}
            }
            $failed=$false
            try{$handoff=New-OfflineReadinessHandoff ([IO.FileInfo]$exe) $config $manifest $root}catch{$failed=$true}
            if($failed -ne ($env:FOUNDRY_SCENARIO -in @('invalid','contradiction'))){throw 'Readiness child status ignored.'}
            if(-not $failed -and ($handoff.Digest -ne (Get-FileSha256 $handoff.Path) -or -not $handoff.Envelope.CanBrowse)){throw 'Frozen envelope missing.'}
            """, scenario);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("renumbered")]
    [InlineData("wrong-device")]
    [InlineData("wrong-media")]
    [InlineData("missing-boot")]
    [InlineData("ambiguous")]
    [InlineData("iso")]
    public async Task UsbSelection_UsesStableIdentityAndBothMarkers(string scenario)
    {
        await RunAsync("""
            $id=[Guid]::NewGuid().ToString('D')
            $identity=@{number=3;uniqueId='stable';serialNumber='serial';friendlyName='fixture';busType='USB';size=100000}
            $actual=@{number=3;uniqueId='stable';serialNumber='serial';friendlyName='fixture';busType='USB';size=100000}
            $manifest=@{mediaId=$id;runtimeIdentifier='win-x64';target='UsbCreate';intendedUsbIdentity=$identity}
            $cache=@{RootPath=(Join-Path $root 'cache');Label='Foundry Cache';DiskNumber=3;DiskIdentity=$actual;MediaId=$id;RuntimeIdentifier='win-x64'}
            $boot=@{RootPath=(Join-Path $root 'boot');Label='BOOT';DiskNumber=3;DiskIdentity=$actual;MediaId=$id;RuntimeIdentifier='win-x64'}
            $volumes=@($cache,$boot)
            switch($env:FOUNDRY_SCENARIO){
                renumbered {$actual.number=7;$cache.DiskNumber=7;$boot.DiskNumber=7}
                wrong-device {$actual.uniqueId='other'}
                wrong-media {$cache.MediaId=[Guid]::NewGuid().ToString()}
                missing-boot {$volumes=@($cache)}
                ambiguous {$volumes+=@{RootPath=(Join-Path $root 'duplicate');Label='Foundry Cache';DiskNumber=3;DiskIdentity=$actual;MediaId=$id;RuntimeIdentifier='win-x64'}}
                iso {$manifest.target='Iso'}
            }
            function Get-BootstrapVolumeCandidates {throw 'Native enumeration must not run.'}
            $selected=Get-UsbCacheRuntimeRoot $manifest $volumes
            if($env:FOUNDRY_SCENARIO -in @('valid','renumbered')) {
                if($selected -ne (Join-Path $cache.RootPath 'Runtime')){throw 'Correct USB was not selected.'}
            } elseif($null -ne $selected){throw 'Untrusted or ambiguous USB was selected.'}
            """, scenario);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("nonce")]
    [InlineData("media")]
    [InlineData("rid")]
    [InlineData("config")]
    [InlineData("revision")]
    [InlineData("replaced")]
    [InlineData("oversized")]
    [InlineData("browse-only")]
    public async Task OfflineEnvelope_IsBoundToCurrentRunAndPinnedCatalogs(string scenario)
    {
        await RunAsync("""
            $config=Join-Path $root 'config.json';[IO.File]::WriteAllText($config,'{}')
            $path=Join-Path $root 'readiness.json';$nonce=[Guid]::NewGuid().ToString('D');$id=[Guid]::NewGuid().ToString('D')
            $manifest=@{mediaId=$id;runtimeIdentifier='win-x64';catalogSnapshots=@(@{id='operating-systems';revision='sha256:fixture'})}
            $result=@{Version=1;Nonce=$nonce;CanBrowse=$true;RuntimeIdentifier='win-x64';Result=@{CanContinue=$true;MediaId=$id;ConfigurationDigest=(Get-FileSha256 $config);CatalogRevisions=@{'operating-systems'='sha256:fixture'};BlockingReasons=@()}}
            [IO.File]::WriteAllText($path,($result|ConvertTo-Json -Depth 8))
            $digest=Get-FileSha256 $path
            switch($env:FOUNDRY_SCENARIO){
                nonce {$result.Nonce=[Guid]::NewGuid().ToString()}
                media {$result.Result.MediaId=[Guid]::NewGuid().ToString()}
                rid {$result.RuntimeIdentifier='win-x86'}
                config {[IO.File]::WriteAllText($config,'changed')}
                revision {$result.Result.CatalogRevisions.'operating-systems'='changed'}
                replaced {$result.Result.BlockingReasons=@('replaced')}
                browse-only {$result.Result.CanContinue=$false;$result.Result.BlockingReasons=@('SelectionIncomplete')}
            }
            [IO.File]::WriteAllText($path,($result|ConvertTo-Json -Depth 8))
            if($env:FOUNDRY_SCENARIO -eq 'oversized'){[IO.File]::WriteAllText($path,('x'*65537))}
            $failed=$false
            try{$verified=Read-OfflineReadinessEnvelope $path $nonce $manifest $config $(if($env:FOUNDRY_SCENARIO -eq 'replaced'){$digest}else{''})}
            catch{$failed=$true}
            if($failed -ne ($env:FOUNDRY_SCENARIO -notin @('valid','browse-only'))){throw 'Readiness binding decision incorrect.'}
            """, scenario);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("dll")]
    [InlineData("deps")]
    [InlineData("missing")]
    [InlineData("reparse")]
    [InlineData("extra")]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    public async Task Runtime_RequiresCompletePinnedFileSet(string scenario)
    {
        await RunAsync("""
            $sourceRoot=Join-Path $root 'source';[IO.Directory]::CreateDirectory($sourceRoot)|Out-Null
            $files=@()
            foreach($name in @('Foundry.Connect.exe','library.dll','Foundry.Connect.deps.json')) {
                $path=Join-Path $sourceRoot $name;[IO.File]::WriteAllText($path,'verified fixture')
                $files+=@{relativePath=$name;length=([IO.FileInfo]$path).Length;sha256=(Get-FileSha256 $path)}
            }
            switch($env:FOUNDRY_SCENARIO){
                dll {[IO.File]::WriteAllText((Join-Path $sourceRoot 'library.dll'),'changed')}
                deps {[IO.File]::WriteAllText((Join-Path $sourceRoot 'Foundry.Connect.deps.json'),'changed')}
                missing {[IO.File]::Delete((Join-Path $sourceRoot 'library.dll'))}
                reparse {function Get-BootstrapFileAttributes {param($path);if($path.EndsWith('library.dll')){return [IO.FileAttributes]::ReparsePoint};return [IO.File]::GetAttributes($path)}}
                extra {[IO.File]::WriteAllText((Join-Path $sourceRoot 'extra.dll'),'extra')}
                traversal {$files[0].relativePath='../outside.exe'}
                duplicate {$files+=$files[0]}
            }
            $valid=Test-RuntimeFiles $sourceRoot $files
            if($valid -ne ($env:FOUNDRY_SCENARIO -eq 'valid')){throw 'Runtime trust decision incorrect.'}
            if($valid){
                $copy=Copy-VerifiedRuntimeToRam $sourceRoot $files (Join-Path $root 'ram') 'Foundry.Connect'
                if(-not $copy.Exists -or -not (Test-RuntimeFiles $copy.DirectoryName $files)){throw 'RAM copy unverified.'}
            }
            """, scenario);
    }

    private static async Task RunAsync(string body, string scenario)
    {
        using var workspace = new TemporaryDirectory();
        string source = WinPeEmbeddedAssetService.ReadEmbeddedText("Foundry.Core.WinPe.FoundryBootstrap");
        string harness = $$"""
            $ErrorActionPreference='Stop'
            $root='{{workspace.Path.Replace("'", "''")}}'
            $source=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(source))}}'))
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Bootstrap syntax invalid.'}
            foreach($definition in $ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
                . ([scriptblock]::Create($definition.Extent.Text))
            }
            {{body}}
            """;
        string path = Path.Combine(workspace.Path, "fixture.ps1");
        File.WriteAllText(path, harness, new UTF8Encoding(true));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["FOUNDRY_SCENARIO"] = scenario;
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
