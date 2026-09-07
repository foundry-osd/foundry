#requires -Version 7.4
param(
    [Parameter(Mandatory)][string[]]$AssetRoots,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SourceSha,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$ProductVersion,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-build\.\d+$')][string]$PackageVersion,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'

function Assert-ReleasePath {
    param([string]$Path)
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'Release inputs require absolute paths.' }
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Release inputs cannot be reparse paths.' }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Read-ReleaseJson {
    param([string]$Path)
    Assert-ReleasePath $Path
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 4194304) { throw 'Release metadata size is invalid.' }
        $document = [Text.Json.JsonDocument]::Parse($stream)
        try {
            $pending = [Collections.Generic.Stack[Text.Json.JsonElement]]::new()
            $pending.Push($document.RootElement)
            while ($pending.Count) {
                $element = $pending.Pop()
                if ($element.ValueKind -eq 'Object') {
                    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                    foreach ($property in $element.EnumerateObject()) {
                        if (-not $names.Add($property.Name)) { throw 'Duplicate release metadata property.' }
                        $pending.Push($property.Value)
                    }
                } elseif ($element.ValueKind -eq 'Array') {
                    foreach ($item in $element.EnumerateArray()) { $pending.Push($item) }
                }
            }
            return $document.RootElement.GetRawText() | ConvertFrom-Json -AsHashtable
        } finally { $document.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-ReleaseIdentity {
    param([string]$Path)
    Assert-ReleasePath $Path
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'Read')
    $sha256 = [Security.Cryptography.SHA256]::Create()
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try {
        if ($stream.Length -le 0) { throw 'Empty release asset.' }
        $size = $stream.Length
        $digest = [Convert]::ToHexString($sha256.ComputeHash($stream)).ToLowerInvariant()
        $stream.Position = 0
        $legacyDigest = [Convert]::ToHexString($sha1.ComputeHash($stream)).ToLowerInvariant()
        return @{ name=[IO.Path]::GetFileName($Path); size=$size; sha256=$digest; sha1=$legacyDigest; path=$Path }
    } finally { $sha256.Dispose(); $sha1.Dispose(); $stream.Dispose() }
}

function Test-FoundryReleaseAssets {
    param([string[]]$AssetRoots,[string]$SourceSha,[string]$ProductVersion,[string]$PackageVersion)
    if ($SourceSha -notmatch '^[a-fA-F0-9]{40}$' -or $ProductVersion -notmatch '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') { throw 'Invalid release identity.' }
    $expectedPackageVersion = '{0}.{1}.{2}-build.{3}' -f $Matches[1],$Matches[2],$Matches[3],$Matches[4]
    if ($PackageVersion -cne $expectedPackageVersion) { throw 'Product/package version mismatch.' }
    $files = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $AssetRoots) {
        Assert-ReleasePath $root
        foreach ($path in [IO.Directory]::EnumerateFiles($root)) {
            $name = [IO.Path]::GetFileName($path)
            if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$') { throw 'Invalid release asset name.' }
            $identity = Get-ReleaseIdentity $path
            if ($files.ContainsKey($name)) {
                if ($files[$name].name -cne $name -or $files[$name].size -ne $identity.size -or $files[$name].sha256 -cne $identity.sha256) { throw 'Conflicting duplicate release asset.' }
            } else { $files.Add($name,$identity) }
        }
    }
    $required = @('FoundrySetup-x64.msi','FoundrySetup-arm64.msi','Foundry.Connect-win-x64.zip','Foundry.Connect-win-arm64.zip','Foundry.Deploy-win-x64.zip','Foundry.Deploy-win-arm64.zip','releases.win-x64.json','releases.win-arm64.json')
    foreach ($name in $required) { if (-not $files.ContainsKey($name) -or $files[$name].name -cne $name) { throw "Required release asset missing: $name" } }
    $owners = @{}
    foreach ($rid in @('win-x64','win-arm64')) {
        $arch=$rid.Substring(4)
        foreach ($name in @("FoundrySetup-$arch.msi","Foundry.Connect-$rid.zip","Foundry.Deploy-$rid.zip","releases.$rid.json")) { $owners[$name]=$rid }
    }
    $references = [Collections.Generic.List[object]]::new()
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $required) { [void]$allowed.Add($name) }
    foreach ($rid in @('win-x64','win-arm64')) {
        $name="build-receipt.$rid.json"
        if (-not $files.ContainsKey($name) -or $files[$name].name -cne $name) { throw 'Missing architecture build receipt.' }
        [void]$allowed.Add($name)
    }
    foreach ($rid in @('win-x64','win-arm64')) {
        $feedName = "releases.$rid.json"
        $feed = Read-ReleaseJson $files[$feedName].path
        if ($feed.Assets -isnot [array] -or $feed.Assets.Count -lt 1 -or $feed.Assets.Count -gt 128) { throw 'Invalid asset feed.' }
        $full = 0
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($asset in $feed.Assets) {
            if ($asset.PackageId -cne 'Foundry' -or $asset.Version -cne $PackageVersion -or $asset.Type -cnotin @('Full','Delta')) { throw 'Unexpected feed package/version/type.' }
            $expectedName = 'Foundry-{0}-{1}-{2}.nupkg' -f $PackageVersion,$rid,$asset.Type.ToLowerInvariant()
            if ($asset.FileName -cne $expectedName -or -not $seen.Add($expectedName) -or -not $files.ContainsKey($expectedName)) { throw 'Missing, duplicate or wrong-architecture feed reference.' }
            $actual = $files[$expectedName]
            if ($asset.Size -isnot [long] -and $asset.Size -isnot [int]) { throw 'Invalid feed size.' }
            if ($asset.Size -ne $actual.size -or $asset.SHA256 -notmatch '^[a-fA-F0-9]{64}$' -or $asset.SHA256 -ine $actual.sha256) { throw 'Feed size/digest mismatch.' }
            if ($asset.SHA1 -and ($asset.SHA1 -notmatch '^[a-fA-F0-9]{40}$' -or $asset.SHA1 -ine $actual.sha1)) { throw 'Feed SHA1 mismatch.' }
            if ($asset.Type -ceq 'Full') { $full++ }
            [void]$allowed.Add($expectedName)
            $owners[$expectedName]=$rid
            $references.Add(@{ feed=$feedName; name=$expectedName; type=$asset.Type; runtimeIdentifier=$rid })
        }
        if ($full -ne 1) { throw 'Each architecture requires exactly one full package.' }
        foreach ($legacyName in @("RELEASES-$rid")) {
            if (-not $files.ContainsKey($legacyName)) { continue }
            [void]$allowed.Add($legacyName)
            $owners[$legacyName]=$rid
            if ($files[$legacyName].size -gt 4194304) { throw 'Legacy release metadata is too large.' }
            foreach ($line in [IO.File]::ReadAllLines($files[$legacyName].path)) {
                if ($line -notmatch '^([a-fA-F0-9]{40}) ([A-Za-z0-9._-]+) ([0-9]+)$') { throw 'Invalid legacy release reference.' }
                $sha=$Matches[1];$name=$Matches[2];$size=$Matches[3]
                if (-not $seen.Contains($name) -or $sha -ine $files[$name].sha1 -or $size -ne [string]$files[$name].size) { throw 'Legacy release reference mismatch.' }
                $references.Add(@{feed=$legacyName;name=$name;runtimeIdentifier=$rid})
            }
        }
    }
    if ($files.ContainsKey('RELEASES')) {
        if ($files['RELEASES'].size -gt 4194304) { throw 'Legacy release metadata is too large.' }
        $legacyOwner = $null
        foreach ($line in [IO.File]::ReadAllLines($files['RELEASES'].path)) {
            if ($line -notmatch '^([a-fA-F0-9]{40}) ([A-Za-z0-9._-]+) ([0-9]+)$') { throw 'Invalid generic legacy reference.' }
            $sha=$Matches[1];$name=$Matches[2];$size=$Matches[3]
            if (-not $owners.ContainsKey($name) -or $name -notlike '*.nupkg' -or $sha -ine $files[$name].sha1 -or $size -ne [string]$files[$name].size) { throw 'Generic legacy reference mismatch.' }
            if ($legacyOwner -and $legacyOwner -cne $owners[$name]) { throw 'Generic legacy feed mixes architecture channels.' }
            $legacyOwner=$owners[$name]
            $references.Add(@{feed='RELEASES';name=$name;runtimeIdentifier=$legacyOwner})
        }
        if (-not $legacyOwner) { throw 'Empty generic legacy feed.' }
        [void]$allowed.Add('RELEASES');$owners['RELEASES']=$legacyOwner
    }
    foreach ($name in $files.Keys) { if (-not $allowed.Contains($name)) { throw "Unqualified emitted release asset: $name" } }
    $qualified = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $architectures = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rid in @('win-x64','win-arm64')) {
        $receipt = Read-ReleaseJson $files["build-receipt.$rid.json"].path
        if ($receipt.schemaVersion -ne 1 -or $receipt.sourceSha -ine $SourceSha -or $receipt.productVersion -cne $ProductVersion -or $receipt.packageVersion -cne $PackageVersion -or
            $receipt.runtimeIdentifier -cne $rid -or -not $architectures.Add($receipt.runtimeIdentifier)) { throw 'Build receipt identity/qualification mismatch.' }
        if ($receipt.assets -isnot [array] -or $receipt.assets.Count -lt 1 -or $receipt.assets.Count -gt 128) { throw 'Invalid receipt asset list.' }
        foreach ($asset in $receipt.assets) {
            if (($asset.length -isnot [long] -and $asset.length -isnot [int]) -or $asset.length -le 0 -or $asset.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid receipt asset length or digest.' }
            if (-not $files.ContainsKey($asset.name) -or $owners[$asset.name] -cne $rid -or $asset.length -ne $files[$asset.name].size -or $asset.sha256 -ine $files[$asset.name].sha256 -or -not $qualified.Add($asset.name)) { throw 'Build receipt asset mismatch.' }
        }
    }
    if ($architectures.Count -ne 2 -or $qualified.Count -ne ($files.Count - 2)) { throw 'Every asset requires matching native build evidence.' }
    return [ordered]@{schemaVersion=1;sourceSha=$SourceSha.ToLowerInvariant();productVersion=$ProductVersion;packageVersion=$PackageVersion;assets=@($files.Values|Sort-Object name|ForEach-Object { [ordered]@{name=$_.name;length=$_.size;sha256=$_.sha256} });feedReferences=@($references)}
}

$manifest = Test-FoundryReleaseAssets -AssetRoots $AssetRoots -SourceSha $SourceSha -ProductVersion $ProductVersion -PackageVersion $PackageVersion
$destination = [IO.Path]::GetFullPath($OutputPath)
Assert-ReleasePath ([IO.Path]::GetDirectoryName($destination))
if ([IO.File]::Exists($destination)) { Assert-ReleasePath $destination }
foreach ($root in $AssetRoots) {
    if ([string]::Equals([IO.Path]::GetDirectoryName($destination).TrimEnd('\','/'),[IO.Path]::GetFullPath($root).TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Write the release manifest outside asset input directories.' }
}
$temp = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temp,($manifest|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temp,$destination,$true)
} finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
$manifest
