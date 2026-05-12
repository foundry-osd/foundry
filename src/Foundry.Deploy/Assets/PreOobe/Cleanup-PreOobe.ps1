$ErrorActionPreference = 'Stop'

$RootFolders = @(
    'C:\DRIVERS',
    'C:\Drivers'
)

function Remove-RootFolder {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Warning "Unable to remove '$Path': $($_.Exception.Message)"
    }
}

foreach ($RootFolder in $RootFolders) {
    Remove-RootFolder -Path $RootFolder
}
