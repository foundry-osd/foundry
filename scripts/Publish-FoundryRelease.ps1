#requires -Version 7.4
param(
    [Parameter(Mandatory)][ValidateSet('Stage', 'Publish')][string]$Mode,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$SourceSha,
    [Parameter(Mandatory)][string]$ProductVersion,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$AssetsDirectory,
    [long]$ExpectedReleaseId,
    [switch]$KeepDraft,
    [string]$NotesFile,
    [scriptblock]$CommandRunner
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

function Invoke-CheckedCommand {
    param([string]$Executable, [string[]]$Arguments)
    if ($CommandRunner) {
        $result = & $CommandRunner $Executable $Arguments
    } else {
        $output = & $Executable @Arguments 2>$null
        $result = @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }
    if ($result.ExitCode -ne 0) { throw "$Executable failed with exit code $($result.ExitCode)." }
    return [string]$result.Output
}

function Assert-ReleasePath {
    param([string]$Path)
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Release inputs cannot redirect through reparse points.'
        }
    }
}

function Read-ReleaseManifest {
    param([IO.Stream]$Stream)
    $document = [Text.Json.JsonDocument]::Parse($Stream)
    try {
        $pending = [Collections.Generic.Stack[Text.Json.JsonElement]]::new()
        $pending.Push($document.RootElement)
        while ($pending.Count -gt 0) {
            $element = $pending.Pop()
            if ($element.ValueKind -eq 'Object') {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($property in $element.EnumerateObject()) {
                    if (-not $names.Add($property.Name)) { throw 'Duplicate release manifest property.' }
                    $pending.Push($property.Value)
                }
            } elseif ($element.ValueKind -eq 'Array') {
                foreach ($item in $element.EnumerateArray()) { $pending.Push($item) }
            }
        }
        return $document.RootElement.GetRawText() | ConvertFrom-Json
    } finally { $document.Dispose() }
}

function Invoke-ReleaseApi {
    param([string]$Endpoint, [string]$Method = 'GET', [object]$Body)
    $arguments = @('api', $Endpoint, '--method', $Method, '--header', 'Accept: application/vnd.github+json')
    if ($null -ne $Body) {
        $bodyPath = Join-Path $scratch ([Guid]::NewGuid().ToString('N') + '.json')
        [IO.File]::WriteAllText($bodyPath, ($Body | ConvertTo-Json -Depth 12 -Compress))
        $arguments += @('--input', $bodyPath)
    }
    $output = Invoke-CheckedCommand 'gh' $arguments
    if ($output.Length -eq 0) { return $null }
    return $output | ConvertFrom-Json
}

function Get-ReleaseAssets {
    param([long]$ReleaseId)
    $json = Invoke-CheckedCommand 'gh' @('api', "repos/$Repository/releases/$ReleaseId/assets?per_page=100", '--paginate', '--slurp')
    foreach ($page in @($json | ConvertFrom-Json)) { foreach ($asset in @($page)) { $asset } }
}

function Assert-TagTarget {
    $output = Invoke-CheckedCommand 'git' @('ls-remote', '--tags', "https://github.com/$Repository.git", "refs/tags/$Tag", "refs/tags/$Tag^{}")
    $refs = @{}
    foreach ($line in @($output -split "`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([a-fA-F0-9]{40})\s+(refs/tags/\S+)$') { throw 'Unexpected remote tag response.' }
        if ($refs.ContainsKey($Matches[2])) { throw 'Duplicate remote tag reference.' }
        $refs[$Matches[2]] = $Matches[1]
    }
    if ($refs.Count -eq 0) { return }
    $resolved = $refs["refs/tags/$Tag^{}"]
    if ($null -eq $resolved) { $resolved = $refs["refs/tags/$Tag"] }
    if ($resolved -cne $SourceSha) { throw 'Existing tag does not resolve to the verified source SHA.' }
}

function Assert-ReleaseBinding {
    param([object]$Release, [long]$Id)
    if ($null -eq $Release -or $Release.id -ne $Id -or $Release.tag_name -cne $Tag -or
        $Release.target_commitish -cne $SourceSha -or $Release.draft -ne $true) {
        throw 'Release identity, target or draft state changed.'
    }
    if ($null -ne $Release.PSObject.Properties['immutable'] -and $Release.immutable -eq $true) {
        throw 'Immutable releases cannot be staged or published by this gate.'
    }
}

function Assert-RemoteAsset {
    param([object]$Asset, [object]$Expected)
    if ($Asset.name -cne $Expected.name -or $Asset.size -ne $Expected.length -or $Asset.state -cne 'uploaded') {
        throw 'Remote asset name, size or upload state does not match the manifest.'
    }
    $digest = if ($null -ne $Asset.PSObject.Properties['digest']) { [string]$Asset.digest } else { '' }
    if ($digest.Length -gt 0) {
        if ($digest -cne ('sha256:' + $Expected.sha256)) { throw 'Remote asset digest does not match the manifest.' }
    } else {
        $downloadDirectory = Join-Path $scratch ([Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($downloadDirectory) | Out-Null
        Invoke-CheckedCommand 'gh' @('release', 'download', $Tag, '--repo', $Repository, '--pattern', $Expected.name, '--dir', $downloadDirectory) | Out-Null
        $downloadPath = Join-Path $downloadDirectory $Expected.name
        if (-not [IO.File]::Exists($downloadPath) -or (Get-Item -LiteralPath $downloadPath).Length -ne $Expected.length -or
            (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Expected.sha256) {
            throw 'Authenticated remote asset download does not match the manifest.'
        }
    }
}

function Assert-RemoteAssets {
    param([object[]]$Assets, [switch]$AllowMissing)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $Assets) {
        if (-not $seen.Add([string]$asset.name) -or -not $expectedAssets.ContainsKey([string]$asset.name)) {
            throw 'Remote release contains a duplicate or unexpected asset.'
        }
        Assert-RemoteAsset $asset $expectedAssets[[string]$asset.name]
    }
    if (-not $AllowMissing -and $seen.Count -ne $expectedAssets.Count) { throw 'Remote release is missing verified assets.' }
}

function Assert-ReadyToPublish {
    param([long]$ReleaseId)
    Assert-RemoteAssets @(Get-ReleaseAssets $ReleaseId)
    Assert-TagTarget
    $release = Invoke-ReleaseApi "repos/$Repository/releases/$ReleaseId"
    Assert-ReleaseBinding $release $ReleaseId
}

$leases = [Collections.Generic.List[IDisposable]]::new()
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$scratch = [IO.Path]::GetFullPath((Join-Path $temporaryParent ('Foundry-release-' + [Guid]::NewGuid().ToString('N'))))
$originalScratch = $scratch
try {
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        $Tag -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' -or
        $SourceSha -cnotmatch '^[a-f0-9]{40}$' -or $ProductVersion -cnotmatch '^\d+\.\d+\.\d+\.\d+$' -or
        $PackageVersion -cnotmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release identity inputs.' }
    $versionParts = $ProductVersion.Split('.')
    $expectedPackageVersion = ($versionParts[0..2] -join '.') + '-build.' + $versionParts[3]
    if ($Tag -cne ('v' + $ProductVersion) -or $PackageVersion -cne $expectedPackageVersion) {
        throw 'Tag, product version and package version do not describe the same release.'
    }
    if ($Mode -eq 'Publish' -and $ExpectedReleaseId -le 0) { throw 'Publish requires the staged release ID.' }
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    $ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
    $AssetsDirectory = [IO.Path]::GetFullPath($AssetsDirectory)
    Assert-ReleasePath $ManifestPath
    Assert-ReleasePath $AssetsDirectory
    $manifestLease = [IO.File]::Open($ManifestPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $leases.Add($manifestLease)
    if ($manifestLease.Length -le 0 -or $manifestLease.Length -gt 4MB) { throw 'Release manifest size is invalid.' }
    $manifest = Read-ReleaseManifest $manifestLease
    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceSha -cne $SourceSha -or
        $manifest.productVersion -cne $ProductVersion -or $manifest.packageVersion -cne $PackageVersion -or
        @($manifest.assets).Count -eq 0 -or @($manifest.assets).Count -gt 256) { throw 'Release manifest identity or asset set is invalid.' }
    $expectedAssets = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @($manifest.assets)) {
        if ($asset.name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $asset.name.Length -gt 200 -or
            $asset.sha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or $asset.length -isnot [ValueType] -or
            [decimal]$asset.length -ne [long]$asset.length -or $asset.length -le 0) { throw 'Invalid release asset entry.' }
        $path = Join-Path $AssetsDirectory $asset.name
        Assert-ReleasePath $path
        $lease = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $leases.Add($lease)
        $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($lease.Length -ne $asset.length -or $digest -cne $asset.sha256.ToLowerInvariant()) { throw 'Local release asset differs from the manifest.' }
        $expectedAssets.Add($asset.name, @{ name = $asset.name; length = $lease.Length; sha256 = $digest; path = $path })
    }
    $manifestName = [IO.Path]::GetFileName($ManifestPath)
    if ($manifestName -cne 'release-assets.json') { throw 'The manifest asset must be named release-assets.json.' }
    $expectedAssets.Add($manifestName, @{ name = $manifestName; length = $manifestLease.Length;
        sha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant(); path = $ManifestPath })

    Assert-TagTarget
    if ($Mode -eq 'Stage') {
        $json = Invoke-CheckedCommand 'gh' @('api', "repos/$Repository/releases?per_page=100", '--paginate', '--slurp')
        $matches = @(foreach ($page in @($json | ConvertFrom-Json)) { foreach ($release in @($page)) { if ($release.tag_name -ceq $Tag) { $release } } })
        if ($matches.Count -gt 1) { throw 'Multiple releases use the requested tag.' }
        if ($matches.Count -eq 0) {
            $notes = if ($NotesFile) { [IO.File]::ReadAllText([IO.Path]::GetFullPath($NotesFile)) } else { '' }
            $release = Invoke-ReleaseApi "repos/$Repository/releases" 'POST' @{ tag_name = $Tag; target_commitish = $SourceSha; name = $Tag; body = $notes; draft = $true }
        } else { $release = $matches[0] }
        $releaseId = [long]$release.id
        if ($releaseId -le 0 -or ($ExpectedReleaseId -gt 0 -and $releaseId -ne $ExpectedReleaseId)) { throw 'Unexpected staged release ID.' }
        Assert-ReleaseBinding $release $releaseId
        $existing = @(Get-ReleaseAssets $releaseId)
        Assert-RemoteAssets $existing -AllowMissing
        foreach ($expected in $expectedAssets.Values) {
            if (@($existing | Where-Object { $_.name -ceq $expected.name }).Count -gt 0) { continue }
            Assert-ReleaseBinding (Invoke-ReleaseApi "repos/$Repository/releases/$releaseId") $releaseId
            $endpoint = "https://uploads.github.com/repos/$Repository/releases/$releaseId/assets?name=$([Uri]::EscapeDataString($expected.name))"
            Invoke-CheckedCommand 'gh' @('api', $endpoint, '--method', 'POST', '--header', 'Content-Type: application/octet-stream', '--input', $expected.path) | Out-Null
        }
        Assert-ReadyToPublish $releaseId
    } else {
        $releaseId = $ExpectedReleaseId
        Assert-ReadyToPublish $releaseId
        if (-not $KeepDraft) {
            $published = Invoke-ReleaseApi "repos/$Repository/releases/$releaseId" 'PATCH' @{ draft = $false }
            if ($published.id -ne $releaseId -or $published.tag_name -cne $Tag -or
                $published.target_commitish -cne $SourceSha -or $published.draft -ne $false) { throw 'Unexpected publication response.' }
        }
    }
    [pscustomobject]@{ releaseId = $releaseId; tag = $Tag; sourceSha = $SourceSha; published = ($Mode -eq 'Publish' -and -not $KeepDraft) }
} finally {
    foreach ($lease in $leases) { $lease.Dispose() }
    if ([IO.Directory]::Exists($originalScratch)) {
        $resolvedScratch = [IO.Path]::GetFullPath($originalScratch)
        if (-not [string]::Equals([IO.Path]::GetDirectoryName($resolvedScratch), $temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.Path]::GetFileName($resolvedScratch).StartsWith('Foundry-release-', [StringComparison]::Ordinal)) {
            throw 'Refusing cleanup outside the owned release scratch directory.'
        }
        Assert-ReleasePath $resolvedScratch
        [IO.Directory]::Delete($resolvedScratch, $true)
    }
}
