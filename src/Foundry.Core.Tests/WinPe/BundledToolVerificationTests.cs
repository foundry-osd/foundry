// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class BundledToolVerificationTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("artifacts")]
    [InlineData("binary")]
    [InlineData("provenance")]
    [InlineData("license")]
    [InlineData("notice")]
    [InlineData("signature")]
    [InlineData("publish")]
    [InlineData("media")]
    public async Task Checker_ValidatesOwnedCopiesWithoutExecutingTools(string scenario)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "Test-FoundryBundledTools.ps1"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var workspace = new TemporaryDirectory();
        string script = Path.Combine(workspace.Path, "fixture.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference='Stop'
            $source=Get-Content -LiteralPath (Join-Path $env:FOUNDRY_REPO 'scripts/Test-FoundryBundledTools.ps1') -Raw
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'Invalid checker syntax.'}
            foreach($definition in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
                . ([scriptblock]::Create($definition.Extent.Text))
            }
            function Get-AuthenticodeSignature {
                param([string]$LiteralPath)
                [pscustomobject]@{Status=$(if($env:FOUNDRY_SCENARIO -eq 'signature'){'NotSigned'}else{'Valid'});SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O=Microsoft Corporation'}}
            }
            $root=Join-Path $PSScriptRoot 'repo'
            $tools=Join-Path $root 'src/Foundry.Core/Assets/7z'
            $registration=Join-Path $root 'src/Foundry.Deploy/Assets/AutopilotRegistration'
            New-Item -ItemType Directory -Path $tools,$registration -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $env:FOUNDRY_REPO 'src/Foundry.Core/Assets/7z') -Destination (Split-Path $tools) -Recurse -Force
            foreach($name in @('ServiceUI.exe','ServiceUI.provenance.json')){Copy-Item -LiteralPath (Join-Path $env:FOUNDRY_REPO ('src/Foundry.Deploy/Assets/AutopilotRegistration/'+$name)) -Destination $registration}
            Copy-Item -LiteralPath (Join-Path $env:FOUNDRY_REPO 'THIRD_PARTY_NOTICES.md') -Destination $root
            $publish=$null;$media=$null
            switch($env:FOUNDRY_SCENARIO){
                artifacts {
                    $publish=Join-Path $root 'publish';$media=Join-Path $root 'media'
                    $mediaTools=Join-Path $media 'Foundry/Tools/7zip'
                    New-Item -ItemType Directory -Path $publish,(Join-Path $mediaTools 'x64') -Force|Out-Null
                    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $publish
                    Copy-Item -LiteralPath (Join-Path $registration 'ServiceUI.provenance.json') -Destination $publish
                    foreach($name in @('License.txt','readme.txt')){Copy-Item -LiteralPath (Join-Path $tools $name) -Destination $mediaTools}
                    Copy-Item -LiteralPath (Join-Path $tools 'x64/7za.exe') -Destination (Join-Path $mediaTools 'x64')
                }
                binary {[IO.File]::WriteAllBytes((Join-Path $registration 'ServiceUI.exe'),[byte[]](1,2,3))}
                provenance {$p=Join-Path $registration 'ServiceUI.provenance.json';$j=Get-Content $p -Raw|ConvertFrom-Json;$j.sha256='0'*64;$j|ConvertTo-Json -Depth 8|Set-Content $p}
                license {[IO.File]::Delete((Join-Path $tools 'License.txt'))}
                notice {[IO.File]::Delete((Join-Path $root 'THIRD_PARTY_NOTICES.md'))}
                publish {$publish=Join-Path $root 'publish';New-Item -ItemType Directory $publish|Out-Null}
                media {$media=Join-Path $root 'media';New-Item -ItemType Directory $media|Out-Null}
            }
            $failed=$false
            try {$result=Test-FoundryBundledToolInventory -RepositoryRoot $root -DeployPublishRoot $publish -WinPeRoot $media -Architecture x64}
            catch {$failed=$true}
            if($env:FOUNDRY_SCENARIO -in @('valid','artifacts')){
                if($failed -or $result.SourceIdentity -ne 'verified' -or $result.SourcePackageVerification -ne 'unverified'){throw 'Expected verified bytes with explicitly unverified source package.'}
            } elseif(-not $failed){throw 'Expected fail-closed verification.'}
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["FOUNDRY_REPO"] = directory.FullName;
        start.Environment["FOUNDRY_SCENARIO"] = scenario;
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
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
