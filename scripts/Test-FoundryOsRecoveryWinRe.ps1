<#
.SYNOPSIS
Validate a mounted or extracted WinRE image for Foundry OS Recovery readiness.

.DESCRIPTION
This script inspects a WinRE working tree for required recovery artifacts and
validates non-recoverable exclusions. Validation is non-destructive by default.
Use `-BootToRecovery` with `-Force` to request a one-time reboot path.

.PARAMETER WinReRoot
Path to the mounted/extracted WinRE image root.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WinReRoot,

    [Parameter()]
    [string[]]$LauncherCandidates = @(
        'Sources\Recovery\Tools\FoundryRecoveryLauncher.cmd',
        'FoundryRecoveryLauncher.cmd',
        'FoundryRecovery.exe',
        'FoundryRecoveryLauncher.exe',
        'Foundry.RecoveryLauncher.exe',
        'Foundry.Recovery.Tool.exe',
        'Foundry.WinRE.Launcher.exe'
    ),

    [Parameter()]
    [string]$WinReConfigFile = 'WinREConfig.xml',

    [Parameter()]
    [string]$BootstrapScript = 'FoundryBootstrap.ps1',

    [Parameter()]
    [string[]]$ExcludedConfigPatterns = @(
        'Foundry\\Config\\foundry.deployment.config.json',
        'Foundry\\Config\\foundry.connect.provisioning-source.txt',
        'Foundry\\Config\\foundry.deploy.provisioning-source.txt',
        'Foundry\\Config\\Secrets\\*',
        'Foundry\\Config\\Network\\*',
        'Foundry\\Config\\Autopilot\\*',
        'Foundry\\Runtime\\AutopilotHash\\*',
        'Foundry\\Tools\\OA3\\*',
        'Foundry\\Config\\*enterprise*.*',
        'Foundry\\Config\\*personalization*.*'
    ),

    [Parameter()]
    [switch]$BootToRecovery,

    [Parameter()]
    [switch]$SkipReAgentC,

    [Parameter()]
    [switch]$Force
)

Set-StrictMode -Version Latest

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)] [string]$Path
    )

    $resolved = Resolve-Path -Path $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        return $Path
    }

    return $resolved.ProviderPath
}

function Test-ExistsAny {
    param(
        [Parameter(Mandatory = $true)] [string]$Root,
        [Parameter(Mandatory = $true)] [string[]]$Candidates,
        [Parameter()] [switch]$Recurse
    )

    foreach ($candidate in $Candidates) {
        $path = Join-Path -Path $Root -ChildPath $candidate
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    if (-not $Recurse) {
        return $null
    }

    $allFiles = Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue
    foreach ($candidate in $Candidates) {
        $found = $allFiles | Where-Object { $_.Name -like $candidate -or $_.FullName -like "*$candidate" } | Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }

    return $null
}

function Test-MissingForbiddenConfigs {
    param(
        [Parameter(Mandatory = $true)] [string]$Root,
        [Parameter(Mandatory = $true)] [string[]]$Patterns
    )

    $files = Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue
    $forbidden = @()

    foreach ($pattern in $Patterns) {
        $normalized = Join-Path -Path $Root -ChildPath $pattern
        $forbidden += $files | Where-Object { $_.FullName -like $normalized }
    }

    return $forbidden | Select-Object -ExpandProperty FullName -Unique
}

function Test-JsonFile {
    param(
        [Parameter(Mandatory = $true)] [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $null = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return $true
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Booting to recovery requires an elevated PowerShell session.'
    }
}

function Test-ReAgentCInfo {
    $reagentc = Join-Path -Path $env:SystemRoot -ChildPath 'System32\reagentc.exe'
    if (-not (Test-Path -LiteralPath $reagentc)) {
        throw "reagentc.exe was not found: $reagentc"
    }

    $output = & $reagentc /info 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "reagentc /info failed with exit code $LASTEXITCODE. $($output -join ' ')"
    }

    $text = $output -join "`n"
    if ($text -notmatch '(?im)Windows RE status:\s*Enabled') {
        throw 'Windows RE is not enabled according to reagentc /info.'
    }

    if ($text -notmatch '(?im)Windows RE location:\s*.+') {
        throw 'Windows RE location is missing from reagentc /info.'
    }

    return $text
}

$checkResults = [System.Collections.Generic.List[string]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$root = Resolve-AbsolutePath -Path $WinReRoot

if (-not (Test-Path -LiteralPath $root)) {
    throw "WinRE root path does not exist: $root"
}

$successCount = 0

if ($SkipReAgentC) {
    $checkResults.Add('SKIP reagentc /info validation')
} else {
    try {
        $null = Test-ReAgentCInfo
        $checkResults.Add('PASS reagentc /info: Windows RE is enabled')
        $successCount++
    } catch {
        $errors.Add("FAIL reagentc /info validation: $($_.Exception.Message)")
    }
}

# Launcher artifact (name-based + regex fallback)
$launcher = Test-ExistsAny -Root $root -Candidates $LauncherCandidates
if (-not $launcher) {
    $launcherCandidatesRegex = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '(?i)foundry.*recovery.*\.(exe|bat|cmd|ps1)$' } |
        Select-Object -First 1

    if ($launcherCandidatesRegex) {
        $launcher = $launcherCandidatesRegex.FullName
    }
}

if ($launcher) {
    $checkResults.Add("PASS Launcher: $launcher")
    $successCount++
} else {
    $errors.Add('FAIL Missing WinRE recovery launcher')
}

# WinREConfig.xml
$winReConfigPath = Test-ExistsAny -Root $root -Candidates @($WinReConfigFile) -Recurse
if ($winReConfigPath) {
    $checkResults.Add("PASS WinREConfig: $winReConfigPath")
    $successCount++
} else {
    $errors.Add('FAIL Missing WinREConfig.xml')
}

# Bootstrap script
$bootstrapPath = Test-ExistsAny -Root $root -Candidates @(
    $BootstrapScript,
    "Windows\\System32\\$BootstrapScript",
    "Windows\\System32\\WinPe\\$BootstrapScript"
) -Recurse
if ($bootstrapPath) {
    $checkResults.Add("PASS Bootstrap: $bootstrapPath")
    $successCount++
} else {
    $errors.Add('FAIL Missing FoundryBootstrap')
}

# Foundry.Connect executable
$connectMatches = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Foundry\\Runtime\\Foundry\.Connect\\[^\\]+\\Foundry\.Connect\.exe$' }
if ($connectMatches) {
    $checkResults.Add("PASS Foundry.Connect: $($connectMatches[0].FullName)")
    $successCount++
} else {
    $errors.Add('FAIL Missing Foundry.Connect executable')
}

# 7-Zip runtime
$sevenZipMatches = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Foundry\\Tools\\7zip\\[^\\]+\\7za\.exe$' }
$sevenZipLicense = Join-Path -Path $root -ChildPath 'Foundry\Tools\7zip\License.txt'
$sevenZipReadme = Join-Path -Path $root -ChildPath 'Foundry\Tools\7zip\readme.txt'
if ($sevenZipMatches -and (Test-Path -LiteralPath $sevenZipLicense) -and (Test-Path -LiteralPath $sevenZipReadme)) {
    $checkResults.Add("PASS 7-Zip runtime: $($sevenZipMatches[0].FullName)")
    $successCount++
} else {
    $errors.Add('FAIL Missing bundled 7-Zip runtime or license files')
}

# Minimal recovery config files
$connectConfigPath = Join-Path -Path $root -ChildPath 'Foundry\Config\foundry.connect.config.json'
$deployConfigPath = Join-Path -Path $root -ChildPath 'Foundry\Config\foundry.deploy.config.json'
$timeZoneMapPath = Join-Path -Path $root -ChildPath 'Foundry\Config\iana-windows-timezones.json'

try {
    if (Test-JsonFile -Path $connectConfigPath) {
        $checkResults.Add("PASS Foundry.Connect config: $connectConfigPath")
        $successCount++
    } else {
        $errors.Add('FAIL Missing or invalid Foundry.Connect recovery config')
    }

    if (Test-JsonFile -Path $deployConfigPath) {
        $checkResults.Add("PASS Foundry.Deploy config: $deployConfigPath")
        $successCount++
    } else {
        $errors.Add('FAIL Missing or invalid sanitized Foundry.Deploy recovery config')
    }

    if (Test-JsonFile -Path $timeZoneMapPath) {
        $checkResults.Add("PASS IANA time zone map: $timeZoneMapPath")
        $successCount++
    } else {
        $errors.Add('FAIL Missing or invalid IANA time zone map')
    }
} catch {
    $errors.Add("FAIL Invalid recovery JSON payload: $($_.Exception.Message)")
}

# Ensure Foundry.Deploy is absent
$deployArtifacts = Get-ChildItem -Path (Join-Path $root 'Foundry') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Foundry\\Runtime\\Foundry\.Deploy\\' -or $_.FullName -match 'Foundry\\Deploy\\' -or $_.Name -match '(?i)foundry\.deploy\.exe$' }
if ($deployArtifacts.Count -eq 0) {
    $checkResults.Add('PASS Foundry.Deploy absent')
    $successCount++
} else {
    $errors.Add(("FAIL Foundry.Deploy present: {0}" -f ($deployArtifacts | Select-Object -First 3 | ForEach-Object { $_.FullName } | Join-String -Separator '; ')))
}

# Excluded configs must be absent
$forbidden = Test-MissingForbiddenConfigs -Root $root -Patterns $ExcludedConfigPatterns
if ($forbidden.Count -eq 0) {
    $checkResults.Add('PASS No excluded recovery-time config files')
    $successCount++
} else {
    $errors.Add(("FAIL Forbidden config file(s) found: {0}" -f (($forbidden[0..([Math]::Min($forbidden.Count - 1, 4))]) -join '; ')))
}

Write-Host 'Foundry OS Recovery WinRE validation' -ForegroundColor Cyan
Write-Host "Root: $root" -ForegroundColor DarkGray
Write-Host ''
foreach ($r in $checkResults) {
    Write-Host $r -ForegroundColor Green
}

if ($errors.Count -gt 0) {
    Write-Host '' 
    Write-Host 'Validation errors:' -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "- $e" -ForegroundColor Red
    }
    Write-Host "`nResult: FAILED (errors: $($errors.Count), passed: $successCount)" -ForegroundColor Red

    if ($BootToRecovery) {
        throw 'Validation failed; boot to recovery skipped by design.'
    }

    exit 1
}

Write-Host "`nResult: PASSED (checks: $successCount)" -ForegroundColor Green

if (-not $BootToRecovery) {
    Write-Host 'Non-destructive mode complete. Use -BootToRecovery -Force to boot into WinRE.'
    return
}

if (-not $Force) {
    throw 'Boot to recovery is explicit and destructive; pass -Force to continue.'
}

Assert-Administrator

if ($PSCmdlet.ShouldProcess('local machine', 'boot into Windows Recovery (OS Recovery)')) {
    Write-Host 'Booting into Windows Recovery Environment...'
    shutdown.exe /r /o /t 0
}
