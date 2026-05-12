param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('LenovoExecutable', 'SurfaceMsi')]
    [string]$CommandKind,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$SuccessExitCodes = @(0, 3010)
$ResolvedPackagePath = [Environment]::ExpandEnvironmentVariables($PackagePath)

function Assert-SuccessExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OperationName
    )

    if ($SuccessExitCodes -notcontains $LASTEXITCODE) {
        throw "$OperationName failed with exit code $LASTEXITCODE."
    }
}

$DriverPathRegistered = $false

try {
    if (-not (Test-Path -Path $ResolvedPackagePath -PathType Leaf)) {
        throw "Driver package was not found: $ResolvedPackagePath"
    }

    switch ($CommandKind) {
        'LenovoExecutable' {
            & $ResolvedPackagePath /SILENT /SUPPRESSMSGBOXES
            Assert-SuccessExitCode -OperationName 'Lenovo driver package'

            reg add 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1' /v Path /t REG_SZ /d 'C:\Drivers' /f | Out-Null
            $DriverPathRegistered = $true
            pnpunattend.exe AuditSystem /L
            Assert-SuccessExitCode -OperationName 'pnpunattend.exe'
        }
        'SurfaceMsi' {
            $logDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\DriverPack'
            New-Item -Path $logDirectory -ItemType Directory -Force | Out-Null

            $logPath = Join-Path $logDirectory 'surface-driverpack.log'
            & msiexec.exe /i $ResolvedPackagePath /qn /norestart /l*v $logPath
            Assert-SuccessExitCode -OperationName 'Surface driver package'
        }
    }
}
finally {
    if ($DriverPathRegistered) {
        reg delete 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1' /v Path /f | Out-Null
    }

    if (Test-Path -Path 'C:\Drivers') {
        Remove-Item -Path 'C:\Drivers' -Recurse -Force
    }

    if (Test-Path -Path $ResolvedPackagePath -PathType Leaf) {
        Remove-Item -Path $ResolvedPackagePath -Force
    }
}
