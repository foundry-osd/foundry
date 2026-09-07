// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class ReleaseAssetVerificationTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("valid-publish")]
    [InlineData("output-preserved")]
    [InlineData("valid-legacy")]
    [InlineData("missing-receipt")]
    [InlineData("receipt-digest")]
    [InlineData("receipt-omission")]
    [InlineData("receipt-string-length")]
    [InlineData("duplicate-property")]
    [InlineData("package-case")]
    [InlineData("missing-runtime")]
    [InlineData("missing-package")]
    [InlineData("feed-version")]
    [InlineData("feed-architecture")]
    [InlineData("feed-hash")]
    [InlineData("feed-size")]
    [InlineData("receipt-sha")]
    [InlineData("receipt-version")]
    [InlineData("receipt-architecture")]
    [InlineData("duplicate-conflict")]
    [InlineData("duplicate-identical")]
    [InlineData("missing-delta")]
    [InlineData("legacy-mismatch")]
    public async Task OfflineAssetContract_RejectsIncompleteOrUnboundInputs(string scenario)
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "scripts", "Test-FoundryReleaseAssets.ps1"))) repo = repo.Parent;
        Assert.NotNull(repo);
        using var workspace = new TemporaryDirectory();
        string harness = Path.Combine(workspace.Path, "fixture.ps1");
        File.WriteAllText(harness, """
            $ErrorActionPreference='Stop'
            $script=Join-Path $env:FOUNDRY_REPO 'scripts/Test-FoundryReleaseAssets.ps1'
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile($script,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Invalid release checker syntax.'}
            foreach($definition in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){. ([scriptblock]::Create($definition.Extent.Text))}
            $root=Join-Path $PSScriptRoot 'assets';$duplicate=Join-Path $PSScriptRoot 'duplicate'
            New-Item -ItemType Directory -Path $root,$duplicate|Out-Null
            $sha='a'*40;$product='26.9.7.1';$package='26.9.7-build.1'
            foreach($rid in @('win-x64','win-arm64')){
                $arch=$rid.Substring(4);$names=@("FoundrySetup-$arch.msi","Foundry.Connect-$rid.zip","Foundry.Deploy-$rid.zip")
                foreach($name in $names){[IO.File]::WriteAllText((Join-Path $root $name),$name)}
                $assets=@()
                foreach($kind in @('Full','Delta')){
                    $name="Foundry-$package-$rid-$($kind.ToLowerInvariant()).nupkg"
                    [IO.File]::WriteAllText((Join-Path $root $name),$name)
                    $file=Get-ReleaseIdentity (Join-Path $root $name)
                    $assets+=@{PackageId='Foundry';Version=$package;Type=$kind;FileName=$name;SHA256=$file.sha256;SHA1=$file.sha1;Size=$file.size}
                    $names+= $name
                }
                $feedName="releases.$rid.json";$names+=$feedName
                @{Assets=$assets}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root $feedName)
                $receiptAssets=@(foreach($name in $names){$file=Get-ReleaseIdentity (Join-Path $root $name);@{name=$name;length=$file.size;sha256=$file.sha256}})
                @{schemaVersion=1;sourceSha=$sha;productVersion=$product;packageVersion=$package;runtimeIdentifier=$rid;assets=$receiptAssets}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root "build-receipt.$rid.json")
            }
            $feedPath=Join-Path $root 'releases.win-x64.json';$feed=Get-Content $feedPath -Raw|ConvertFrom-Json -AsHashtable
            $receiptPath=Join-Path $root 'build-receipt.win-x64.json';$receipt=Get-Content $receiptPath -Raw|ConvertFrom-Json -AsHashtable
            switch($env:FOUNDRY_SCENARIO){
                valid-legacy {
                    $name='RELEASES-win-x64';$asset=$feed.Assets[0]
                    [IO.File]::WriteAllText((Join-Path $root $name),($asset.SHA1+' '+$asset.FileName+' '+$asset.Size))
                    $file=Get-ReleaseIdentity (Join-Path $root $name)
                    $receipt.assets+=@{name=$name;length=$file.size;sha256=$file.sha256}
                    $receipt|ConvertTo-Json -Depth 8|Set-Content $receiptPath
                }
                missing-receipt {[IO.File]::Delete($receiptPath)}
                receipt-digest {$receipt.assets[0].sha256='0'*64}
                receipt-omission {$receipt.assets=@($receipt.assets|Select-Object -Skip 1)}
                receipt-string-length {$receipt.assets[0].length=[string]$receipt.assets[0].length}
                duplicate-property {[IO.File]::WriteAllText($feedPath,'{"Assets":[],"assets":[]}')}
                package-case {
                    $name=$feed.Assets[0].FileName
                    $original=Join-Path $root $name;$temporary=$original+'.tmp'
                    [IO.File]::Move($original,$temporary)
                    [IO.File]::Move($temporary,(Join-Path $root $name.ToLowerInvariant()))
                }
                output-preserved {[IO.File]::Delete((Join-Path $root 'Foundry.Deploy-win-arm64.zip'))}
                missing-runtime {[IO.File]::Delete((Join-Path $root 'Foundry.Deploy-win-arm64.zip'))}
                missing-package {[IO.File]::Delete((Join-Path $root $feed.Assets[0].FileName))}
                missing-delta {[IO.File]::Delete((Join-Path $root $feed.Assets[1].FileName))}
                feed-version {$feed.Assets[0].Version='26.9.7-build.2'}
                feed-architecture {$feed.Assets[0].FileName=$feed.Assets[0].FileName.Replace('x64','arm64')}
                feed-hash {$feed.Assets[0].SHA256='0'*64}
                feed-size {$feed.Assets[0].Size++}
                receipt-sha {$receipt.sourceSha='b'*40}
                receipt-version {$receipt.productVersion='26.9.7.2'}
                receipt-architecture {$receipt.runtimeIdentifier='win-arm64'}
                duplicate-conflict {[IO.File]::WriteAllText((Join-Path $duplicate 'FoundrySetup-x64.msi'),'different')}
                duplicate-identical {Copy-Item -LiteralPath (Join-Path $root 'FoundrySetup-x64.msi') -Destination $duplicate}
                legacy-mismatch {[IO.File]::WriteAllText((Join-Path $root 'RELEASES-win-x64'),(('0'*40)+' '+$feed.Assets[0].FileName+' 1'))}
            }
            if($env:FOUNDRY_SCENARIO.StartsWith('feed-')){$feed|ConvertTo-Json -Depth 8|Set-Content $feedPath}
            if($env:FOUNDRY_SCENARIO.StartsWith('receipt-')){$receipt|ConvertTo-Json -Depth 8|Set-Content $receiptPath}
            $output=Join-Path $PSScriptRoot 'manifest.json'
            [IO.File]::WriteAllText($output,'prior manifest')
            $failed=$false
            try {
                if($env:FOUNDRY_SCENARIO -in @('valid-publish','output-preserved')) {
                    $result=& $script -AssetRoots @($root,$duplicate) -SourceSha $sha -ProductVersion $product -PackageVersion $package -OutputPath $output
                } else {$result=Test-FoundryReleaseAssets -AssetRoots @($root,$duplicate) -SourceSha $sha -ProductVersion $product -PackageVersion $package}
            } catch {$failed=$true}
            if($env:FOUNDRY_SCENARIO -eq 'output-preserved' -and [IO.File]::ReadAllText($output) -cne 'prior manifest'){throw 'Previous manifest was modified after validation failure.'}
            if($env:FOUNDRY_SCENARIO -eq 'valid-publish' -and (Get-Content $output -Raw|ConvertFrom-Json).sourceSha -ne $sha){throw 'Manifest was not published.'}
            if($env:FOUNDRY_SCENARIO -in @('valid','valid-publish','valid-legacy','duplicate-identical')){
                if($failed -or $result.assets.Count -ne $(if($env:FOUNDRY_SCENARIO -eq 'valid-legacy'){15}else{14}) -or $result.sourceSha -ne $sha){throw 'Expected complete bound manifest.'}
            }elseif(-not $failed){throw 'Expected release validation failure.'}
            """, new UTF8Encoding(false));
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["FOUNDRY_REPO"] = repo.FullName;
        start.Environment["FOUNDRY_SCENARIO"] = scenario;
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-File", harness }) start.ArgumentList.Add(arg);
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
