$ErrorActionPreference = 'Stop'
$OperationTimeoutSeconds = 15
$WindowsApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f'
$Stage = 'discovery'

function Write-ActivationLog {
    param([string]$Message)

    $sessionId = [guid]::Empty
    [void][guid]::TryParse($env:FOUNDRY_DIAGNOSTIC_SESSION_ID, [ref]$sessionId)
    Write-Host ('[{0}] Application=Foundry.Deploy Session={1} Component=WindowsOemActivation {2}' -f
        [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'), $sessionId, $Message)
}

function Get-WindowsLicense {
    @(Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "ApplicationID='$WindowsApplicationId' AND PartialProductKey IS NOT NULL" -OperationTimeoutSec $OperationTimeoutSeconds -ErrorAction Stop |
        Where-Object { $_.LicenseIsAddon -eq $false -and $_.LicenseFamily -eq $Edition -and -not [string]::IsNullOrWhiteSpace($_.PartialProductKey) })
}

function Invoke-LicensingMethod {
    param($InputObject, [string]$MethodName, [hashtable]$Arguments = @{})

    $result = Invoke-CimMethod -InputObject $InputObject -MethodName $MethodName -Arguments $Arguments -OperationTimeoutSec $OperationTimeoutSeconds -ErrorAction Stop
    if ($null -eq $result.ReturnValue) {
        throw 'Licensing method did not return a status.'
    }
    if ($result.ReturnValue -ne 0) {
        $hresult = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$result.ReturnValue), 0)
        throw [Runtime.InteropServices.COMException]::new('Licensing operation failed.', $hresult)
    }
}

try {
    Write-ActivationLog 'Windows OEM activation started.'
    $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem -OperationTimeoutSec $OperationTimeoutSeconds -ErrorAction Stop
    if ($operatingSystem.ProductType -ne 1) {
        Write-ActivationLog 'Skipped: operating system is not a Windows client.'
        return
    }

    $Edition = [string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name EditionID).EditionID
    # Only known setup keys are replaceable; an unactivated retail key can still belong to the administrator.
    $defaultKeySuffixes = @{
        Core = '8HVX7'
        CoreN = 'WXCHW'
        CoreSingleLanguage = '6F4BT'
        CoreCountrySpecific = '8TYMD'
        Professional = '3V66T'
        ProfessionalN = 'PKCKT'
    }
    if (-not $defaultKeySuffixes.ContainsKey($Edition)) {
        Write-ActivationLog 'Skipped: unsupported Windows edition.'
        return
    }

    $products = @(Get-WindowsLicense)
    if ($products.Count -ne 1 -or [string]::IsNullOrWhiteSpace($products[0].ID) -or
        $null -eq $products[0].LicenseStatus -or $products[0].LicenseStatus -notin 0..6) {
        Write-ActivationLog 'Skipped: installed Windows licensing state is ambiguous.'
        return
    }
    $product = $products[0]
    if ($product.LicenseStatus -eq 1) {
        Write-ActivationLog 'Skipped: Windows is already activated.'
        return
    }
    $service = Get-CimInstance -ClassName SoftwareLicensingService -OperationTimeoutSec $OperationTimeoutSeconds -ErrorAction Stop
    if ($product.ProductKeyChannel -like 'Volume*' -or $product.Description -match '\bVOLUME\b' -or
        -not [string]::IsNullOrWhiteSpace($product.KeyManagementServiceMachine) -or
        -not [string]::IsNullOrWhiteSpace($service.KeyManagementServiceMachine) -or $service.IsKeyManagementServiceMachine -eq 1) {
        Write-ActivationLog 'Skipped: preserving volume licensing configuration.'
        return
    }

    $firmwareKey = [string]$service.OA3xOriginalProductKey
    if ([string]::IsNullOrWhiteSpace($firmwareKey)) {
        Write-ActivationLog 'Skipped: no firmware key is available.'
        return
    }
    if ($firmwareKey -notmatch '^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$') {
        Write-ActivationLog 'Skipped: firmware key format is not recognized.'
        return
    }
    # Exact edition tokens avoid treating ProfessionalN or CoreSingleLanguage as their parent edition.
    $firmwareDescriptionPattern = '(?i)(?:^|\s)' + [regex]::Escape($Edition) + '\s+OEM:DM\s*$'
    if ([string]$service.OA3xOriginalProductKeyDescription -notmatch $firmwareDescriptionPattern) {
        Write-ActivationLog 'Skipped: firmware edition is incompatible or could not be verified.'
        return
    }

    $firmwareKeySuffix = $firmwareKey.Substring($firmwareKey.Length - 5)
    $firmwareKeyIsInstalled = $product.ProductKeyChannel -eq 'OEM:DM' -and $product.PartialProductKey -eq $firmwareKeySuffix
    $defaultKeyIsInstalled = $product.ProductKeyChannel -eq 'Retail' -and $product.PartialProductKey -eq $defaultKeySuffixes[$Edition]
    if (-not $firmwareKeyIsInstalled -and -not $defaultKeyIsInstalled) {
        Write-ActivationLog 'Skipped: preserving the existing product key.'
        return
    }

    if (-not $firmwareKeyIsInstalled) {
        $Stage = 'install'
        Invoke-LicensingMethod -InputObject $service -MethodName InstallProductKey -Arguments @{ ProductKey = $firmwareKey }
        Write-ActivationLog 'Firmware product key installed.'
        $Stage = 'refresh'
        Invoke-LicensingMethod -InputObject $service -MethodName RefreshLicenseStatus
        $products = @(Get-WindowsLicense | Where-Object { $_.ProductKeyChannel -eq 'OEM:DM' -and $_.PartialProductKey -eq $firmwareKeySuffix })
        if ($products.Count -ne 1) {
            Write-ActivationLog 'Stopped: installed firmware key could not be verified.'
            return
        }
        $product = $products[0]
    }

    if ($product.LicenseStatus -ne 1) {
        $Stage = 'activate'
        Invoke-LicensingMethod -InputObject $product -MethodName Activate
        $Stage = 'refresh'
        Invoke-LicensingMethod -InputObject $service -MethodName RefreshLicenseStatus
        $products = @(Get-WindowsLicense | Where-Object { $_.ID -eq $product.ID -and $_.ProductKeyChannel -eq 'OEM:DM' -and $_.PartialProductKey -eq $firmwareKeySuffix })
        if ($products.Count -ne 1 -or $products[0].LicenseStatus -ne 1) {
            Write-ActivationLog 'Firmware key is installed, but Windows remains unactivated. Check activation after connecting to the Internet.'
            return
        }
    }

    Write-ActivationLog 'Windows OEM activation succeeded.'
}
catch {
    # Provider errors can contain the key or method arguments. Never serialize the error record.
    Write-ActivationLog ('Windows OEM activation did not complete; stage={0}; HRESULT=0x{1:X8}. Setup will continue.' -f $Stage, $_.Exception.HResult)
}
finally {
    $firmwareKey = $null
    $service = $null
}
