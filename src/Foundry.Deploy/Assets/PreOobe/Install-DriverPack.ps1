param(
    [Parameter(Mandatory=$true)][ValidateSet('LenovoExecutable','SurfaceMsi')][string]$CommandKind,
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][long]$ExpectedSizeBytes
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command Invoke-FoundryInstaller -ErrorAction SilentlyContinue)) {
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'Foundry-PreOobeFunctions.ps1')
}
$ResolvedPackagePath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($PackagePath))
$PackageLock = $null
$InstallationCompleted = $false
$RebootRequired = $false
$DriverPathRegistered = $false
$PriorDriverPath = $null
$NativeExitCode = $null
$Failure = $null
$RestoreFailed = $false

function Invoke-DriverOperation {
    param([string]$FilePath,[string[]]$Arguments,[switch]$AllowReboot)
    $code = Invoke-FoundryInstaller -FilePath $FilePath -Arguments $Arguments -TimeoutSeconds 1800
    $script:NativeExitCode = [int]$code
    if ($code -eq 3010 -and $AllowReboot) { $script:RebootRequired=$true; return }
    if ($code -ne 0) { throw 'driver_native_exit_failed' }
}
function Assert-DriverPackageTrust {
    param([string]$Path, [string]$Kind)

    # Exact subjects qualified from official driver-package signature tables; never accept a substring match.
    $expectedSubject = switch ($Kind) {
        'LenovoExecutable' { 'CN=Lenovo, OU=G10, O=Lenovo, L=Morrisville, S=North Carolina, C=US' }
        'SurfaceMsi' { 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US' }
        default { throw 'Trusted publisher policy is unavailable for this driver package family.' }
    }

    $job = Start-Job -ScriptBlock {
        param($FilePath)
        $ErrorActionPreference = 'Stop'
        $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $FilePath
        [pscustomobject]@{ Status = $signature.Status.ToString(); Subject = $signature.SignerCertificate.Subject }
    } -ArgumentList $Path

    try {
        if (-not (Wait-Job -Job $job -Timeout 120)) {
            throw 'Driver package signature verification exceeded its two-minute deadline.'
        }
        if ($job.State -ne 'Completed') {
            throw 'Windows could not complete the driver package signature check.'
        }
        $result = @(Receive-Job -Job $job -ErrorAction Stop)
        if ($result.Count -ne 1 -or $result[0].Status -cne 'Valid' -or $result[0].Subject -cne $expectedSubject) {
            throw 'The driver package does not have a valid signature from the expected publisher.'
        }
    }
    finally {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
}

try {
    $PackageLock = [IO.File]::Open($ResolvedPackagePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    if ($ExpectedSizeBytes -le 0 -or $PackageLock.Length -ne $ExpectedSizeBytes) { throw 'driver_identity_failed' }
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $actualHash = [BitConverter]::ToString($hasher.ComputeHash($PackageLock)).Replace('-','') }
    finally { $hasher.Dispose() }
    if ($actualHash -ine $ExpectedSha256) { throw 'driver_identity_failed' }
    Assert-DriverPackageTrust -Path $ResolvedPackagePath -Kind $CommandKind
    switch ($CommandKind) {
        'LenovoExecutable' {
            Invoke-DriverOperation -FilePath $ResolvedPackagePath -Arguments @('/SILENT','/SUPPRESSMSGBOXES') -AllowReboot
            $PriorDriverPath = Get-FoundryDriverPath
            $DriverPathRegistered = $true
            Set-FoundryDriverPath -State ([pscustomobject]@{Exists=$true;Value='C:\Drivers';Kind='String'})
            Invoke-DriverOperation -FilePath (Join-Path $env:SystemRoot 'System32\pnpunattend.exe') -Arguments @('AuditSystem','/L')
        }
        'SurfaceMsi' {
            $logDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\DriverPack'
            New-Item -Path $logDirectory -ItemType Directory -Force | Out-Null
            Invoke-DriverOperation -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -Arguments @('/i',$ResolvedPackagePath,'/qn','/norestart','/l*v',(Join-Path $logDirectory 'surface-driverpack.log')) -AllowReboot
        }
    }
    $InstallationCompleted = $true
}
catch { $Failure = $_ }
finally {
    try {
        if ($DriverPathRegistered) {
            try { Set-FoundryDriverPath -State $PriorDriverPath }
            catch { $InstallationCompleted=$false; $RestoreFailed=$true; if($null -eq $Failure) { $Failure=$_ } }
        }
    }
    finally {
        if ($null -ne $PackageLock) { $PackageLock.Dispose() }
        if ($InstallationCompleted -and [IO.File]::Exists($ResolvedPackagePath)) {
            try { Remove-Item -LiteralPath $ResolvedPackagePath -Force -ErrorAction Stop }
            catch { $InstallationCompleted=$false; $Failure=$_ }
        }
        Write-FoundryDriverOutcome ([pscustomobject]@{exitCode=$NativeExitCode;rebootRequired=$RebootRequired;failed=(-not $InstallationCompleted);cleanupFailed=$RestoreFailed})
    }
}
if ($null -ne $Failure) {
    if ($null -ne $NativeExitCode -and $NativeExitCode -ne 0 -and $NativeExitCode -ne 3010) { exit $NativeExitCode }
    throw $Failure
}
if ($RebootRequired) { exit 3010 }
