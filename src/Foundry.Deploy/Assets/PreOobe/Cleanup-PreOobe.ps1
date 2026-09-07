$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Foundry-PreOobeFunctions.ps1')
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'pre-oobe-manifest.json') -Raw | ConvertFrom-Json
foreach ($action in @($manifest.scripts)) {
    Remove-FoundryActionInputs -Action $action -Root $root
}
