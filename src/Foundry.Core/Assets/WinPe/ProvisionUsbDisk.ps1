$ErrorActionPreference = 'Stop'

Import-Module Storage

function Write-FoundryUsbProgress([int]$Percent, [string]$Status) {
    Write-Output ("FOUNDRY_USB_PROGRESS|{0}|{1}" -f $Percent, $Status)
}

function Write-FoundryUsbVerbose([string]$Message) {
    Write-Output ("FOUNDRY_USB_VERBOSE|{0}" -f $Message)
}

function Wait-FoundryUsbVolume([char]$DriveLetter, [string]$VolumeName) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $volume = Get-Volume -DriveLetter $DriveLetter -ErrorAction SilentlyContinue
        if ($null -ne $volume) {
            Write-FoundryUsbVerbose "$VolumeName volume is available. DriveLetter=$DriveLetter, FileSystem=$($volume.FileSystem), Size=$($volume.Size)."
            return
        }

        Start-Sleep -Milliseconds 500
        Update-HostStorageCache -ErrorAction SilentlyContinue
        Update-Disk -Number $diskNumber -ErrorAction SilentlyContinue
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for $VolumeName volume $DriveLetter`: to become available."
}

$diskNumber = {{DISK_NUMBER}}
$partitionStyle = '{{PARTITION_STYLE}}'
$fullFormat = {{FULL_FORMAT}}
$bootDriveLetter = '{{BOOT_DRIVE_LETTER}}'
$cacheDriveLetter = '{{CACHE_DRIVE_LETTER}}'

Write-FoundryUsbVerbose "Provisioning disk $diskNumber. PartitionStyle=$partitionStyle, FullFormat=$fullFormat, BootDriveLetter=$bootDriveLetter, CacheDriveLetter=$cacheDriveLetter."

Write-FoundryUsbProgress 21 'Opening USB disk.'
$disk = Get-Disk -Number $diskNumber -ErrorAction Stop
Write-FoundryUsbVerbose "Disk opened. Number=$($disk.Number), FriendlyName=$($disk.FriendlyName), PartitionStyle=$($disk.PartitionStyle), Size=$($disk.Size), IsOffline=$($disk.IsOffline), IsReadOnly=$($disk.IsReadOnly)."

Write-FoundryUsbProgress 23 'Preparing USB disk attributes.'
if ($disk.IsOffline) { Set-Disk -Number $diskNumber -IsOffline $false -ErrorAction Stop }
if ($disk.IsReadOnly) { Set-Disk -Number $diskNumber -IsReadOnly $false -ErrorAction Stop }
Write-FoundryUsbVerbose 'USB disk attributes prepared.'

Write-FoundryUsbProgress 26 'Clearing USB partition table.'
Clear-Disk -Number $diskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop
Update-HostStorageCache -ErrorAction SilentlyContinue
Update-Disk -Number $diskNumber -ErrorAction SilentlyContinue
Write-FoundryUsbVerbose 'USB partition table cleared and host storage cache refreshed.'

Write-FoundryUsbProgress 32 'Initializing USB partition table.'
$disk = Get-Disk -Number $diskNumber -ErrorAction Stop
if ($disk.PartitionStyle -eq 'RAW') {
    Initialize-Disk -Number $diskNumber -PartitionStyle $partitionStyle -ErrorAction Stop
} elseif ([string]$disk.PartitionStyle -ne $partitionStyle) {
    Write-FoundryUsbVerbose "Disk partition style remains $($disk.PartitionStyle); using diskpart to reset to $partitionStyle."
    $diskPartResetScriptPath = Join-Path $PWD 'foundry-usb-reset.txt'
    $diskPartResetCommands = @(
        "select disk $diskNumber",
        'clean',
        "convert $($partitionStyle.ToLowerInvariant())"
    )
    Set-Content -Path $diskPartResetScriptPath -Value $diskPartResetCommands -Encoding ASCII -Force
    try {
        & diskpart.exe /s $diskPartResetScriptPath | ForEach-Object { Write-FoundryUsbVerbose $_ }
        if ($LASTEXITCODE -ne 0) { throw "diskpart reset failed with exit code $LASTEXITCODE." }
    } finally {
        Remove-Item -Path $diskPartResetScriptPath -Force -ErrorAction SilentlyContinue
    }
}

Update-HostStorageCache -ErrorAction SilentlyContinue
Update-Disk -Number $diskNumber -ErrorAction SilentlyContinue
$disk = Get-Disk -Number $diskNumber -ErrorAction Stop
if ([string]$disk.PartitionStyle -ne $partitionStyle) {
    throw "Disk $diskNumber still reports partition style $($disk.PartitionStyle) after requesting $partitionStyle."
}
Write-FoundryUsbVerbose "USB partition table initialized. CurrentPartitionStyle=$($disk.PartitionStyle)."

Write-FoundryUsbProgress 38 'Creating BOOT partition.'
$bootPartition = New-Partition -DiskNumber $diskNumber -Size 4096MB -DriveLetter $bootDriveLetter -ErrorAction Stop
Write-FoundryUsbVerbose "BOOT partition created. PartitionNumber=$($bootPartition.PartitionNumber), DriveLetter=$($bootPartition.DriveLetter), Size=$($bootPartition.Size)."
{{ACTIVE_BOOT_PARTITION}}
if ($partitionStyle -eq 'MBR') { Write-FoundryUsbVerbose "BOOT partition marked active. PartitionNumber=$($bootPartition.PartitionNumber)." }
Wait-FoundryUsbVolume -DriveLetter $bootDriveLetter -VolumeName 'BOOT'

Write-FoundryUsbProgress 44 'Formatting BOOT partition.'
$bootFormatArguments = @{
    DriveLetter = $bootDriveLetter
    FileSystem = 'FAT32'
    NewFileSystemLabel = 'BOOT'
    Confirm = $false
    Force = $true
    ErrorAction = 'Stop'
}
if ($fullFormat) { $bootFormatArguments['Full'] = $true }
Format-Volume @bootFormatArguments | Out-Null
Write-FoundryUsbVerbose "BOOT partition formatted. DriveLetter=$bootDriveLetter, FileSystem=FAT32, Label=BOOT."

Write-FoundryUsbProgress 49 'Creating cache partition.'
$cachePartition = New-Partition -DiskNumber $diskNumber -UseMaximumSize -DriveLetter $cacheDriveLetter -ErrorAction Stop
Write-FoundryUsbVerbose "Cache partition created. PartitionNumber=$($cachePartition.PartitionNumber), DriveLetter=$($cachePartition.DriveLetter), Size=$($cachePartition.Size)."
Wait-FoundryUsbVolume -DriveLetter $cacheDriveLetter -VolumeName 'cache'

Write-FoundryUsbProgress 53 'Formatting cache partition.'
$cacheFormatArguments = @{
    DriveLetter = $cacheDriveLetter
    FileSystem = 'NTFS'
    NewFileSystemLabel = 'Foundry Cache'
    Confirm = $false
    Force = $true
    ErrorAction = 'Stop'
}
if ($fullFormat) { $cacheFormatArguments['Full'] = $true }
Format-Volume @cacheFormatArguments | Out-Null
Write-FoundryUsbVerbose "Cache partition formatted. DriveLetter=$cacheDriveLetter, FileSystem=NTFS, Label=Foundry Cache."

Write-FoundryUsbProgress 55 'USB partitions formatted.'
[pscustomobject]@{
    DiskNumber = $diskNumber
    BootDriveLetter = "$bootDriveLetter`:"
    CacheDriveLetter = "$cacheDriveLetter`:"
} | ConvertTo-Json -Compress
