$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'src\Foundry.slnx'
$toolManifestPath = Join-Path $repoRoot '.config\dotnet-tools.json'
$xamlFiles = git -C $repoRoot ls-files '*.xaml'
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list tracked XAML files.'
}

dotnet format whitespace $solutionPath --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format whitespace verification failed. Run scripts\Format-Foundry.ps1.'
}

dotnet format style $solutionPath --diagnostics IDE0073 --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format style IDE0073 verification failed. Run scripts\Format-Foundry.ps1.'
}

dotnet tool restore --tool-manifest $toolManifestPath
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet tool restore failed.'
}

Push-Location $repoRoot
try {
    if ($xamlFiles.Count -gt 0) {
        dotnet tool run xstyler -- -f ($xamlFiles -join ',') -p -c .xamlstyler -l Verbose
        if ($LASTEXITCODE -ne 0) {
            throw 'XAML formatting verification failed. Run scripts\Format-Foundry.ps1.'
        }
    }
}
finally {
    Pop-Location
}
