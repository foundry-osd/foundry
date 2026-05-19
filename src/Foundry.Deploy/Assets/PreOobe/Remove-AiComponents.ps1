[CmdletBinding()]
param(
    [string]$SettingsPath = "$env:SystemRoot\Temp\Foundry\PreOobe\Data\Remove-AiComponents.settings.json"
)

$ErrorActionPreference = 'Stop'
$script:ScriptStartedAt = [DateTimeOffset]::Now
$script:TranscriptStarted = $false

function Start-FoundryTranscript {
    $logRoot = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
    New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
    $transcriptPath = Join-Path $logRoot 'Remove-AiComponents.transcript.log'

    try {
        Start-Transcript -Path $transcriptPath -Force | Out-Null
        $script:TranscriptStarted = $true
    }
    catch {
        Write-Warning "Unable to start transcript '$transcriptPath': $($_.Exception.Message)"
    }
}

function Stop-FoundryTranscript {
    if (-not $script:TranscriptStarted) {
        return
    }

    try {
        Stop-Transcript | Out-Null
    }
    catch {
        Write-Warning "Unable to stop transcript: $($_.Exception.Message)"
    }
}

function Write-FoundryLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $now = [DateTimeOffset]::Now
    $elapsed = $now - $script:ScriptStartedAt
    Write-Host ("[{0:O}] [+{1:hh\:mm\:ss}] {2}" -f $now, $elapsed, $Message)
}

function Get-FoundryAiComponentRemovalSettings {
    if (-not (Test-Path -Path $SettingsPath)) {
        throw "AI component removal settings file '$SettingsPath' was not found."
    }

    return Get-Content -Path $SettingsPath -Raw | ConvertFrom-Json
}

function Test-FoundrySettingEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $Settings.PSObject.Properties[$Name]
    return $property -isnot $null -and [bool]$property.Value
}

function Invoke-FoundryAction {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    try {
        Write-FoundryLog "Starting $Name."
        & $Action
        Write-FoundryLog "Completed $Name."
    }
    catch {
        Write-Warning "$Name failed: $($_.Exception.Message)"
    }
}

function Set-FoundryRegistryDwordValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [int]$Value
    )

    New-Item -Path $Path -Force | Out-Null
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
}

function Invoke-FoundryRegExe {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = Start-Process -FilePath 'reg.exe' -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "reg.exe $($Arguments -join ' ') failed with exit code $($process.ExitCode)."
    }
}

function Mount-FoundryDefaultUserHive {
    $defaultUserHivePath = Join-Path $env:SystemDrive 'Users\Default\NTUSER.DAT'
    if (-not (Test-Path -Path $defaultUserHivePath)) {
        throw "Default user hive '$defaultUserHivePath' was not found."
    }

    if (Test-Path -Path 'Registry::HKEY_USERS\FoundryDefaultUser') {
        Write-FoundryLog 'Default user hive is already mounted at HKU\FoundryDefaultUser.'
        return $false
    }

    Invoke-FoundryRegExe -Arguments @('load', 'HKU\FoundryDefaultUser', $defaultUserHivePath)
    Write-FoundryLog "Mounted default user hive '$defaultUserHivePath' at HKU\FoundryDefaultUser."
    return $true
}

function Dismount-FoundryDefaultUserHive {
    Invoke-FoundryRegExe -Arguments @('unload', 'HKU\FoundryDefaultUser')
    Write-FoundryLog 'Unmounted default user hive HKU\FoundryDefaultUser.'
}

function Remove-FoundryProvisionedAppxPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CatalogPackageName
    )

    $packages = Get-AppxProvisionedPackage -Online |
        Where-Object { $_.PackageName -like "$CatalogPackageName*" -or $_.DisplayName -eq $CatalogPackageName }

    if (-not $packages) {
        Write-FoundryLog "Provisioned AppX package '$CatalogPackageName' was not found."
        return
    }

    foreach ($package in $packages) {
        $resolvedPackageName = [string]$package.PackageName
        Write-FoundryLog "Removing provisioned AppX package '$resolvedPackageName'."
        $removeArguments = @{
            Online = $true
            PackageName = $resolvedPackageName
            ErrorAction = 'Stop'
        }

        Remove-AppxProvisionedPackage @removeArguments | Out-Null
    }
}

function Disable-FoundryCopilot {
    Remove-FoundryProvisionedAppxPackage -CatalogPackageName 'Microsoft.Copilot'
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' -Name 'TurnOffWindowsCopilot' -Value 1
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_USERS\FoundryDefaultUser\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'ShowCopilotButton' -Value 0
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_USERS\FoundryDefaultUser\Software\Policies\Microsoft\Windows\WindowsCopilot' -Name 'TurnOffWindowsCopilot' -Value 1
}

function Disable-FoundryRecall {
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableAIDataAnalysis' -Value 1
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' -Name 'AllowRecallEnablement' -Value 0
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' -Name 'TurnOffSavingSnapshots' -Value 1
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_USERS\FoundryDefaultUser\Software\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableAIDataAnalysis' -Value 1
}

function Disable-FoundryClickToDo {
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableClickToDo' -Value 1
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_USERS\FoundryDefaultUser\Software\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableClickToDo' -Value 1
}

function Disable-FoundryAiServiceAutoStart {
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc' -Name 'Start' -Value 3
}

function Disable-FoundryEdgeAi {
    $edgePolicyPath = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge'
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'CopilotCDPPageContext' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'CopilotPageContext' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'HubsSidebarEnabled' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'EdgeEntraCopilotPageContext' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'EdgeHistoryAISearchEnabled' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'ComposeInlineEnabled' -Value 0
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'GenAILocalFoundationalModelSettings' -Value 1
    Set-FoundryRegistryDwordValue -Path $edgePolicyPath -Name 'NewTabPageBingChatEnabled' -Value 0
}

function Disable-FoundryPaintAi {
    $paintPolicyPath = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint'
    Set-FoundryRegistryDwordValue -Path $paintPolicyPath -Name 'DisableCocreator' -Value 1
    Set-FoundryRegistryDwordValue -Path $paintPolicyPath -Name 'DisableGenerativeFill' -Value 1
    Set-FoundryRegistryDwordValue -Path $paintPolicyPath -Name 'DisableImageCreator' -Value 1
    Set-FoundryRegistryDwordValue -Path $paintPolicyPath -Name 'DisableGenerativeErase' -Value 1
    Set-FoundryRegistryDwordValue -Path $paintPolicyPath -Name 'DisableRemoveBackground' -Value 1
}

function Disable-FoundryNotepadAi {
    Set-FoundryRegistryDwordValue -Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\WindowsNotepad' -Name 'DisableAIFeatures' -Value 1
}

Start-FoundryTranscript
try {
    Write-FoundryLog "Loading AI component removal settings from '$SettingsPath'."
    $settings = Get-FoundryAiComponentRemovalSettings
    $requiresDefaultUserHive = (Test-FoundrySettingEnabled -Settings $settings -Name 'removeCopilot') -or
        (Test-FoundrySettingEnabled -Settings $settings -Name 'disableRecall') -or
        (Test-FoundrySettingEnabled -Settings $settings -Name 'disableClickToDo')
    $mountedDefaultUserHive = $false

    try {
        if ($requiresDefaultUserHive) {
            $mountedDefaultUserHive = Mount-FoundryDefaultUserHive
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'removeCopilot') {
            Invoke-FoundryAction -Name 'Remove Microsoft Copilot' -Action { Disable-FoundryCopilot }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'removeAiHub') {
            Invoke-FoundryAction -Name 'Remove Copilot+ AI Hub' -Action { Remove-FoundryProvisionedAppxPackage -CatalogPackageName 'Microsoft.Windows.AIHub' }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disableRecall') {
            Invoke-FoundryAction -Name 'Disable Windows Recall' -Action { Disable-FoundryRecall }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disableClickToDo') {
            Invoke-FoundryAction -Name 'Disable Click to Do' -Action { Disable-FoundryClickToDo }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disableAiServiceAutoStart') {
            Invoke-FoundryAction -Name 'Disable Windows AI service autostart' -Action { Disable-FoundryAiServiceAutoStart }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disableEdgeAi') {
            Invoke-FoundryAction -Name 'Disable Microsoft Edge AI features' -Action { Disable-FoundryEdgeAi }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disablePaintAi') {
            Invoke-FoundryAction -Name 'Disable Paint AI features' -Action { Disable-FoundryPaintAi }
        }

        if (Test-FoundrySettingEnabled -Settings $settings -Name 'disableNotepadAi') {
            Invoke-FoundryAction -Name 'Disable Notepad AI features' -Action { Disable-FoundryNotepadAi }
        }
    }
    finally {
        if ($mountedDefaultUserHive) {
            Dismount-FoundryDefaultUserHive
        }
    }

    Write-FoundryLog 'AI component removal completed.'
}
finally {
    Stop-FoundryTranscript
}
