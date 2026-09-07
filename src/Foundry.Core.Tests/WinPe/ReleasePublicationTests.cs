// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class ReleasePublicationTests
{
    [Theory]
    [InlineData("stage")]
    [InlineData("new-tag")]
    [InlineData("annotated-tag")]
    [InlineData("reuse-draft")]
    [InlineData("tag-version-mismatch")]
    [InlineData("package-version-mismatch")]
    [InlineData("duplicate-manifest-property")]
    [InlineData("upload-failure")]
    [InlineData("public-collision")]
    [InlineData("immutable-collision")]
    [InlineData("digest-mismatch")]
    [InlineData("requested-draft")]
    [InlineData("publish")]
    [InlineData("recheck-failure")]
    [InlineData("tag-mismatch")]
    [InlineData("local-mismatch")]
    [InlineData("download-fallback")]
    [InlineData("download-mismatch")]
    public async Task Gate_RequiresVerifiedDraftAndBytesBeforePublication(string scenario)
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "scripts", "Publish-FoundryRelease.ps1")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        using var workspace = new TemporaryDirectory();
        string fixture = Path.Combine(workspace.Path, "release-fixture.ps1");
        File.WriteAllText(fixture, Fixture, new UTF8Encoding(true));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["FOUNDRY_RELEASE_SCRIPT"] = Path.Combine(repository.FullName, "scripts", "Publish-FoundryRelease.ps1");
        start.Environment["FOUNDRY_RELEASE_CASE"] = scenario;
        start.Environment["FOUNDRY_RELEASE_ROOT"] = workspace.Path;
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    private const string Fixture = """
        $ErrorActionPreference = 'Stop'
        $case = $env:FOUNDRY_RELEASE_CASE
        $root = $env:FOUNDRY_RELEASE_ROOT
        $sha = 'a' * 40
        $tag = 'v2026.9.7.0'
        $payload = Join-Path $root 'Foundry.Setup.nupkg'
        [IO.File]::WriteAllText($payload, 'synthetic verified package')
        $manifestPath = Join-Path $root 'release-assets.json'
        $entry = @{ name = 'Foundry.Setup.nupkg'; length = (Get-Item $payload).Length; sha256 = (Get-FileHash $payload).Hash.ToLowerInvariant() }
        @{ schemaVersion = 1; sourceSha = $sha; productVersion = '2026.9.7.0'; packageVersion = '2026.9.7-build.0'; assets = @($entry) } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        $release = [pscustomobject]@{ id = 91; tag_name = $tag; target_commitish = $sha; draft = $true; immutable = $false }
        $state = @{ release = $release; assets = [Collections.ArrayList]::new(); uploads = 0; publishes = 0; creates = 0; downloads = 0; calls = 0 }
        $publishMode = $case -in @('requested-draft','publish','recheck-failure','download-fallback','download-mismatch')
        if ($publishMode -or $case -in @('digest-mismatch','reuse-draft')) {
            foreach ($path in @($payload, $manifestPath)) {
                $digest = 'sha256:' + (Get-FileHash $path).Hash.ToLowerInvariant()
                if ($case -eq 'digest-mismatch') { $digest = 'sha256:' + ('0' * 64) }
                if ($case -in @('download-fallback','download-mismatch')) { $digest = $null }
                $state.assets.Add([pscustomobject]@{ id = $state.assets.Count + 1; name = [IO.Path]::GetFileName($path); size = (Get-Item $path).Length; state = 'uploaded'; digest = $digest }) | Out-Null
            }
        }
        if ($case -eq 'public-collision') { $release.draft = $false }
        if ($case -eq 'immutable-collision') { $release.immutable = $true }
        if ($case -eq 'local-mismatch') { [IO.File]::WriteAllText($payload, 'corrupt') }
        if ($case -eq 'duplicate-manifest-property') {
            $json = [IO.File]::ReadAllText($manifestPath).Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1')
            [IO.File]::WriteAllText($manifestPath, $json)
        }
        $runner = {
            param($executable, $arguments)
            $state.calls++
            if ($executable -eq 'git') {
                if ($case -eq 'new-tag') { return @{ ExitCode = 0; Output = '' } }
                if ($case -eq 'annotated-tag') { return @{ ExitCode = 0; Output = "$('b' * 40)`trefs/tags/$tag`n$sha`trefs/tags/$tag^{}" } }
                $target = if ($case -eq 'tag-mismatch') { 'b' * 40 } else { $sha }
                return @{ ExitCode = 0; Output = "$target`trefs/tags/$tag" }
            }
            if ($executable -ne 'gh') { throw 'Unexpected executable.' }
            if ($arguments[0] -eq 'release') {
                if ($arguments[1] -ne 'download') { throw 'Unexpected release command.' }
                $state.downloads++
                $name = $arguments[[Array]::IndexOf($arguments, '--pattern') + 1]
                $destination = Join-Path $arguments[[Array]::IndexOf($arguments, '--dir') + 1] $name
                if ($case -eq 'download-mismatch') { [IO.File]::WriteAllText($destination, 'corrupt') }
                else { [IO.File]::Copy((Join-Path $root $name), $destination) }
                return @{ ExitCode = 0; Output = '' }
            }
            $endpoint = $arguments[1]
            $methodIndex = [Array]::IndexOf($arguments, '--method')
            $method = if ($methodIndex -ge 0) { $arguments[$methodIndex + 1] } else { 'GET' }
            if ($endpoint -like 'https://uploads.github.com/*') {
                $state.uploads++
                if ($case -eq 'upload-failure') { return @{ ExitCode = 7; Output = '' } }
                if ($state.release.draft -ne $true) { throw 'Upload was attempted against public release.' }
                $path = $arguments[[Array]::IndexOf($arguments, '--input') + 1]
                $name = [Uri]::UnescapeDataString(($endpoint -split '\?name=')[1])
                $state.assets.Add([pscustomobject]@{ id = $state.assets.Count + 1; name = $name; size = (Get-Item $path).Length; state = 'uploaded'; digest = 'sha256:' + (Get-FileHash $path).Hash.ToLowerInvariant() }) | Out-Null
                return @{ ExitCode = 0; Output = '{}' }
            }
            if ($endpoint -like '*/assets?per_page=100') {
                return @{ ExitCode = 0; Output = (ConvertTo-Json -InputObject @(@($state.assets.ToArray())) -Depth 8 -Compress) }
            }
            if ($endpoint -like '*/releases?per_page=100') {
                $existing = if ($case -in @('public-collision','immutable-collision','digest-mismatch','reuse-draft')) { @($state.release) } else { @() }
                return @{ ExitCode = 0; Output = (ConvertTo-Json -InputObject @($existing) -Depth 8 -Compress) }
            }
            if ($method -eq 'POST') {
                $body = Get-Content -LiteralPath $arguments[[Array]::IndexOf($arguments, '--input') + 1] -Raw | ConvertFrom-Json
                if ($body.draft -ne $true -or $body.target_commitish -cne $sha) { throw 'Creation did not pin a draft to exact source.' }
                $state.creates++
            } elseif ($method -eq 'PATCH') {
                $state.publishes++
                $body = Get-Content -LiteralPath $arguments[[Array]::IndexOf($arguments, '--input') + 1] -Raw | ConvertFrom-Json
                if ($body.draft -ne $false) { throw 'Unexpected final publication body.' }
                $state.release.draft = $false
            } elseif ($case -eq 'recheck-failure') { $state.release.target_commitish = 'b' * 40 }
            return @{ ExitCode = 0; Output = ($state.release | ConvertTo-Json -Compress) }
        }.GetNewClosure()
        $parameters = @{ Mode = $(if ($publishMode) { 'Publish' } else { 'Stage' }); Repository = 'synthetic/owned'; Tag = $tag;
            SourceSha = $sha; ProductVersion = '2026.9.7.0'; PackageVersion = '2026.9.7-build.0'; ManifestPath = $manifestPath;
            AssetsDirectory = $root; CommandRunner = $runner }
        if ($publishMode) { $parameters.ExpectedReleaseId = 91 }
        if ($case -eq 'tag-version-mismatch') { $parameters.Tag = 'v2026.9.8.0' }
        if ($case -eq 'package-version-mismatch') { $parameters.PackageVersion = '2026.9.7-build.1' }
        if ($case -eq 'requested-draft') { $parameters.KeepDraft = $true }
        $failure = $null
        try { $result = & $env:FOUNDRY_RELEASE_SCRIPT @parameters } catch { $failure = $_ }
        $success = $case -in @('stage','new-tag','annotated-tag','reuse-draft','requested-draft','publish','download-fallback')
        if ($success -and $null -ne $failure) { throw $failure }
        if (-not $success -and $null -eq $failure) { throw 'Expected fail-closed publication gate.' }
        $expectedPublishes = if ($case -in @('publish','download-fallback')) { 1 } else { 0 }
        if ($state.publishes -ne $expectedPublishes) { throw 'Publication count violates the gate.' }
        if ($case -in @('stage','new-tag','annotated-tag') -and ($state.creates -ne 1 -or $state.uploads -ne 2 -or $result.releaseId -ne 91 -or $result.published)) { throw 'Staging did not upload exactly package and manifest to a draft.' }
        if ($case -eq 'reuse-draft' -and ($state.creates -ne 0 -or $state.uploads -ne 0 -or $result.releaseId -ne 91 -or $result.published)) { throw 'Verified draft was mutated instead of reused.' }
        if ($case -eq 'requested-draft' -and ($state.release.draft -ne $true -or $result.published)) { throw 'Requested draft was published.' }
        if ($case -in @('public-collision','immutable-collision','digest-mismatch','tag-mismatch','local-mismatch') -and $state.uploads -ne 0) { throw 'Unverified bytes were uploaded.' }
        if ($case -in @('local-mismatch','tag-version-mismatch','package-version-mismatch','duplicate-manifest-property') -and $state.calls -ne 0) { throw 'Local mismatch reached external command boundary.' }
        if ($case -eq 'download-fallback' -and $state.downloads -ne 2) { throw 'Missing digests did not verify downloaded bytes.' }
        """;
}
