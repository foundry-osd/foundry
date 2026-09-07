param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DeployPublishRoot,
    [string]$WinPeRoot,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'

function Get-FoundryBundled7ZipInventory {
    @'
[
  {
    "path": "7za.dll",
    "size": 293376,
    "sha256": "56AC93E6FA3CBAB0181A450A089EF9F6E0F9F4B5CEBE0B628D22B9A67FDD827F",
    "machine": "014C",
    "version": "26.02"
  },
  {
    "path": "7za.exe",
    "size": 859136,
    "sha256": "BFB34635F295DF13EA1677C0D51D08FAFD2DA21A1C0BEA252DF6342D40E511A0",
    "machine": "014C",
    "version": "26.02"
  },
  {
    "path": "7zxa.dll",
    "size": 163328,
    "sha256": "51F674885E32636D1D08E054BAF14A4BB3A7DD3823598AEC1C4F61A946EF45D0",
    "machine": "014C",
    "version": "26.02"
  },
  {
    "path": "Far/7-ZipFar.dll",
    "size": 287744,
    "sha256": "F0BFD6C0264DC16611DBD7EA524ED3EAACF83CFB5E14C047C0E1B0426E8107F9",
    "machine": "014C",
    "version": "26.02"
  },
  {
    "path": "Far/7-ZipFar64.dll",
    "size": 479232,
    "sha256": "44951C94EE4895428A488045BCEA71EFA10EFB8B9C1BC0922F3718E03B7CB413",
    "machine": "8664",
    "version": "26.02"
  },
  {
    "path": "arm64/7-ZipFar.dll",
    "size": 478720,
    "sha256": "67C444A69DBE78D652BEDE6827C1F974874B77A74246A29099398992FF61C136",
    "machine": "AA64",
    "version": "26.02"
  },
  {
    "path": "arm64/7za.dll",
    "size": 452608,
    "sha256": "1190036CC18EDD256F1A9CDC7B54EE37D9FAD119A301BFC2D6E3F59BD85EFAC6",
    "machine": "AA64",
    "version": "26.02"
  },
  {
    "path": "arm64/7za.exe",
    "size": 1206272,
    "sha256": "CADBD34657713935222EB14FDDBCDD51953501B44C749D9A029FAB8F1C46BE7E",
    "machine": "AA64",
    "version": "26.02"
  },
  {
    "path": "arm64/7zxa.dll",
    "size": 292864,
    "sha256": "06264440A37D731B11E2FBAA5556A8F639C142FE9CF46D4486619862E08924BE",
    "machine": "AA64",
    "version": "26.02"
  },
  {
    "path": "x64/7za.dll",
    "size": 416256,
    "sha256": "8105EAB695801F9C9FCC234C7963A7AC217378916821618DFB9D97B04562B82E",
    "machine": "8664",
    "version": "26.02"
  },
  {
    "path": "x64/7za.exe",
    "size": 1334784,
    "sha256": "35D4D69D7CD6CB44558F208C3B1334268013F9DAF82D2DDA848893A1C30C59C2",
    "machine": "8664",
    "version": "26.02"
  },
  {
    "path": "x64/7zxa.dll",
    "size": 218112,
    "sha256": "E729E2F0188DA0A40EE065F1A26797278CB88E9507413080F0F5A4F7F4DB6C1E",
    "machine": "8664",
    "version": "26.02"
  }
]
'@ | ConvertFrom-Json
}

function Assert-FoundryOrdinaryFile {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\')) { throw 'Remote paths are not supported by this offline checker.' }
    $cursor = $full
    while ($cursor) {
        $attributes = [IO.File]::GetAttributes($cursor)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Bundled tool verification rejects reparse paths.' }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    if (-not [IO.File]::Exists($full)) { throw 'A required bundled-tool file is missing.' }
    return $full
}

function Get-FoundryCheckedHash {
    param([string]$Path)
    $full = Assert-FoundryOrdinaryFile $Path
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $hash.Dispose(); $stream.Dispose() }
}

function Assert-FoundryBundledBinary {
    param([string]$Path, [long]$Size, [string]$Sha256, [string]$Machine, [string]$Version, [switch]$MicrosoftSigned)
    $full = Assert-FoundryOrdinaryFile $Path
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = New-Object IO.BinaryReader($stream)
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        if ($stream.Length -ne $Size) { throw 'Bundled binary length mismatch.' }
        $actualHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '')
        if ($actualHash -cne $Sha256) { throw 'Bundled binary SHA-256 mismatch.' }
        $stream.Position = 0
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Bundled binary has no DOS header.' }
        $stream.Position = 60
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -gt ($stream.Length - 6)) { throw 'Bundled binary PE header is invalid.' }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16().ToString('X4') -cne $Machine) { throw 'Bundled binary architecture mismatch.' }
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($full)
        if ($MicrosoftSigned) {
            $actualVersion = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart
        } else { $actualVersion = $info.FileVersion }
        if ($actualVersion -cne $Version) { throw 'Bundled binary version mismatch.' }
        if ($MicrosoftSigned) {
            $signature = Get-AuthenticodeSignature -LiteralPath $full
            if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
                $signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=Microsoft Corporation(,|$)') {
                throw 'ServiceUI requires a valid Microsoft Corporation Authenticode signature.'
            }
        }
    } finally { $hash.Dispose(); $reader.Dispose(); $stream.Dispose() }
}

function Assert-FoundryMatchingFile {
    param([string]$Source, [string]$Destination)
    if ((Get-FoundryCheckedHash $Source) -cne (Get-FoundryCheckedHash $Destination)) { throw 'Packaged bundled-tool notice or payload mismatch.' }
}

function Test-FoundryBundledToolInventory {
    param([string]$RepositoryRoot, [string]$DeployPublishRoot, [string]$WinPeRoot, [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64')
    if (-not [IO.Path]::IsPathRooted($RepositoryRoot)) { throw 'An absolute repository root is required.' }
    $registration = Join-Path $RepositoryRoot 'src/Foundry.Deploy/Assets/AutopilotRegistration'
    $provenancePath = Assert-FoundryOrdinaryFile (Join-Path $registration 'ServiceUI.provenance.json')
    if ((Get-Item -LiteralPath $provenancePath).Length -gt 16384) { throw 'ServiceUI provenance is too large.' }
    $record = [IO.File]::ReadAllText($provenancePath) | ConvertFrom-Json
    $serviceHash = '1BE85A64AAD2C3CAA0DC28705B49A1548E85157F4D2D522C20FEC4B4570A623F'
    if ($record.schemaVersion -ne 1 -or $record.file -cne 'ServiceUI.exe' -or $record.size -ne 74008 -or
        $record.sha256 -cne $serviceHash -or $record.fileVersion -cne '1.3.0.0' -or $record.peMachine -cne '8664' -or
        $record.architecture -cne 'x64' -or $record.expectedSigner -cne 'Microsoft Corporation' -or
        $record.sourceFamily -cne 'Microsoft Deployment Toolkit' -or $null -ne $record.sourcePackage -or
        $record.sourceUrl -cne 'https://learn.microsoft.com/en-us/intune/configmgr/mdt/' -or
        $record.sourcePackageVerification -cne 'unverified' -or $record.redistributionVerification -cne 'unverified') {
        throw 'ServiceUI provenance does not match the reviewed identity and evidence limits.'
    }
    Assert-FoundryBundledBinary (Join-Path $registration 'ServiceUI.exe') 74008 $serviceHash '8664' '1.3.0.0' -MicrosoftSigned
    $tools = Join-Path $RepositoryRoot 'src/Foundry.Core/Assets/7z'
    foreach ($item in (Get-FoundryBundled7ZipInventory)) {
        Assert-FoundryBundledBinary (Join-Path $tools $item.path) $item.size $item.sha256 $item.machine $item.version
    }
    $license = Join-Path $tools 'License.txt'
    $readme = Join-Path $tools 'readme.txt'
    if ((Get-FoundryCheckedHash $license) -cne '39D2187B942CC88818DD959E53C6B7DDDD152097A42061467C4D56050168D646' -or
        (Get-FoundryCheckedHash $readme) -cne '34A97F93FC953B5DB05FC121BE2B959244065595B2F076FADE1FFC156B6D479E') { throw 'Bundled 7-Zip license/readme mismatch.' }
    $notice = Assert-FoundryOrdinaryFile (Join-Path $RepositoryRoot 'THIRD_PARTY_NOTICES.md')
    if ((Get-Item -LiteralPath $notice).Length -eq 0) { throw 'Third-party notice is empty.' }
    if ($DeployPublishRoot) {
        Assert-FoundryMatchingFile $notice (Join-Path $DeployPublishRoot 'THIRD_PARTY_NOTICES.md')
        Assert-FoundryMatchingFile $provenancePath (Join-Path $DeployPublishRoot 'ServiceUI.provenance.json')
    }
    if ($WinPeRoot) {
        $mediaTools = Join-Path $WinPeRoot 'Foundry/Tools/7zip'
        Assert-FoundryMatchingFile $license (Join-Path $mediaTools 'License.txt')
        Assert-FoundryMatchingFile $readme (Join-Path $mediaTools 'readme.txt')
        Assert-FoundryMatchingFile (Join-Path $tools ($Architecture + '/7za.exe')) (Join-Path $mediaTools ($Architecture + '/7za.exe'))
    }
    [pscustomobject]@{
        SourceIdentity = 'verified'
        SourcePackageVerification = 'unverified'
        RedistributionVerification = 'unverified'
        DeployPublishScope = $(if ($DeployPublishRoot) { 'external notices and provenance verified; embedded executable not inspected' } else { 'not supplied' })
        WinPeScope = $(if ($WinPeRoot) { 'selected 7-Zip payload and licenses verified' } else { 'not supplied' })
        NativeLaunchQualification = 'not performed'
    }
}

Test-FoundryBundledToolInventory -RepositoryRoot $RepositoryRoot -DeployPublishRoot $DeployPublishRoot -WinPeRoot $WinPeRoot -Architecture $Architecture
