$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'src\Foundry.slnx'

dotnet format whitespace $solutionPath --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format whitespace verification failed. Run scripts\Format-Foundry.ps1.'
}

dotnet format style $solutionPath --diagnostics IDE0073 --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format style IDE0073 verification failed. Run scripts\Format-Foundry.ps1.'
}
