#requires -Version 7.4
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$RuntimeIdentifier,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SourceSha,
    [Parameter(Mandatory)][string]$ProductVersion,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$ReleaseNotesPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$architecture = $RuntimeIdentifier.Substring(4)
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant() -cne $architecture) {
    throw 'Release packages must be built and validated on their native architecture.'
}
$head = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -cne $SourceSha) { throw 'Checkout does not match the release SHA.' }
$changed = @(git -C $repoRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or @($changed | Where-Object { $_ -notmatch '^ M src/Directory\.Build\.props$' }).Count) {
    throw 'Release source must be clean except for the applied build version.'
}
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'src/Directory.Build.props') -Raw
$originalProps = git -C $repoRoot show "${SourceSha}:src/Directory.Build.props"
if ($LASTEXITCODE -ne 0) { throw 'Unable to read committed version properties.' }
$expectedProps = ($originalProps -join "`n") + "`n"
foreach ($property in @('Version', 'AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    if ($props.SelectSingleNode("/Project/PropertyGroup/$property").InnerText -cne $ProductVersion) {
        throw 'Build version has not been applied consistently.'
    }
    $pattern = "(?<=<$property>)[^<]+(?=</$property>)"
    if ([regex]::Matches($expectedProps, $pattern).Count -ne 1) { throw 'Committed version properties are ambiguous.' }
    $expectedProps = [regex]::Replace($expectedProps, $pattern, $ProductVersion)
}
$actualProps = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Directory.Build.props')).Replace("`r`n", "`n")
if ($actualProps -cne $expectedProps) { throw 'Build properties contain changes beyond the applied version.' }
$parts = $ProductVersion.Split('.')
if ($parts.Count -ne 4 -or $PackageVersion -cne "$($parts[0]).$($parts[1]).$($parts[2])-build.$($parts[3])") {
    throw 'Package version does not match product version.'
}

$output = Join-Path $repoRoot "artifacts/release/$RuntimeIdentifier"
if (Test-Path -LiteralPath $output) { throw 'Release output already exists; use a fresh checkout.' }
$work = Join-Path $repoRoot "artifacts/release-validation/$RuntimeIdentifier-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $output, $work | Out-Null

& (Join-Path $PSScriptRoot 'Publish-FoundryVelopack.ps1') -RuntimeIdentifier $RuntimeIdentifier -PackVersion $PackageVersion -ReleaseNotesPath $ReleaseNotesPath
$velopackOutput = Join-Path $repoRoot 'artifacts/velopack/release-assets'
$feedName = "releases.$RuntimeIdentifier.json"
$feed = Get-Content -LiteralPath (Join-Path $velopackOutput $feedName) -Raw | ConvertFrom-Json
$feed.Assets = @($feed.Assets | Where-Object { $_.Version -ceq $PackageVersion })
if (@($feed.Assets | Where-Object Type -eq Full).Count -ne 1) { throw 'Expected exactly one current full package.' }
$feed | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output $feedName)
Copy-Item -LiteralPath (Join-Path $velopackOutput "FoundrySetup-$architecture.msi") -Destination $output
$packageNames = @($feed.Assets | ForEach-Object {
    if ($_.FileName -cnotmatch ('^Foundry-' + [regex]::Escape($PackageVersion) + '-' + $RuntimeIdentifier + '-(full|delta)\.nupkg$')) {
        throw 'Unexpected package identity in feed.'
    }
    Copy-Item -LiteralPath (Join-Path $velopackOutput $_.FileName) -Destination $output
    $_.FileName
})
$legacyLines = @{}
foreach ($legacy in @(Get-ChildItem -LiteralPath $velopackOutput -File | Where-Object { $_.Name -in @('RELEASES', "RELEASES-$RuntimeIdentifier") })) {
    $lines = @(foreach ($entry in (Get-Content -LiteralPath $legacy.FullName)) {
        $fields = $entry.Trim() -split '\s+'
        if ($fields.Count -ne 3 -or $fields[0] -notmatch '^[a-fA-F0-9]{40}$' -or $fields[2] -notmatch '^\d+$') {
            throw 'Malformed legacy release metadata.'
        }
        if ($fields[1] -cin $packageNames) { $fields -join ' ' }
    })
    foreach ($line in $lines) {
        $name = ($line -split '\s+')[1]
        if ($legacyLines.ContainsKey($name) -and $legacyLines[$name] -cne $line) { throw 'Conflicting legacy feed entry.' }
        $legacyLines[$name] = $line
    }
}
if ($legacyLines.Count) { Set-Content -LiteralPath (Join-Path $output "RELEASES-$RuntimeIdentifier") -Value @($legacyLines.Values | Sort-Object) }

$oldExtractionRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    foreach ($application in @('Connect', 'Deploy')) {
        & (Join-Path $PSScriptRoot "Publish-Foundry$application.ps1") -RuntimeIdentifier $RuntimeIdentifier
        $publish = Join-Path $repoRoot "artifacts/publish/Foundry.$application/$RuntimeIdentifier"
        $archive = Join-Path $output "Foundry.$application-$RuntimeIdentifier.zip"
        Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal
        $extracted = Join-Path $work $application
        Expand-Archive -LiteralPath $archive -DestinationPath $extracted
        $executable = Join-Path $extracted "Foundry.$application.exe"
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Runtime archive is missing its executable.' }
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $work "bundle-$application"
        New-Item -ItemType Directory -Path $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR | Out-Null
        $process = Start-Process -FilePath $executable -ArgumentList @('--validate-package', '--expected-version', $ProductVersion, '--expected-runtime', $RuntimeIdentifier) -WindowStyle Hidden -PassThru
        try {
            if (-not $process.WaitForExit(60000)) {
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw "Foundry.$application validation process exit is unconfirmed; retain $work." }
                throw "Foundry.$application package validation timed out."
            }
            if ($process.ExitCode -ne 0) { throw "Foundry.$application package validation failed ($($process.ExitCode))." }
        } finally { $process.Dispose() }
        if ($application -eq 'Deploy') {
            & (Join-Path $PSScriptRoot 'Test-FoundryBundledTools.ps1') -DeployPublishRoot $extracted -Architecture $architecture | Out-Null
        }
    }
} finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldExtractionRoot }

$assets = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ name = $_.Name; length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$receipt = [ordered]@{
    schemaVersion = 1
    sourceSha = $SourceSha
    productVersion = $ProductVersion
    packageVersion = $PackageVersion
    runtimeIdentifier = $RuntimeIdentifier
    assets = $assets
}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output "build-receipt.$RuntimeIdentifier.json")
Write-Host "Verified native release candidates: $output"
