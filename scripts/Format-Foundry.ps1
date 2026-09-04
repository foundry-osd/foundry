param(
    [switch]$VerifyResourceFormatting,
    [string[]]$ResourceFiles
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'src\Foundry.slnx'
$toolManifestPath = Join-Path $repoRoot '.config\dotnet-tools.json'
if (-not $PSBoundParameters.ContainsKey('ResourceFiles')) {
    $ResourceFiles = git -C $repoRoot ls-files '*.resw' '*.resx'
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to list tracked resource files.'
    }
}

function Format-ResourceWhitespaceNode {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode]$Node,

        [Parameter(Mandatory)]
        [string]$CarriageReturnToken
    )

    foreach ($childNode in @($Node.ChildNodes)) {
        $isTextNode = $childNode.NodeType -in @(
            [System.Xml.XmlNodeType]::Text,
            [System.Xml.XmlNodeType]::Whitespace,
            [System.Xml.XmlNodeType]::SignificantWhitespace)

        if ($isTextNode -and
            $Node.LocalName -notin @('value', 'comment') -and
            [string]::IsNullOrWhiteSpace($childNode.Value)) {
            [void]$Node.RemoveChild($childNode)
        }
        elseif ($isTextNode -and $Node.LocalName -eq 'value' -and $childNode.Value.Contains("`r")) {
            $childNode.Value = $childNode.Value.Replace("`r", $CarriageReturnToken)
        }
        elseif ($childNode.HasChildNodes) {
            Format-ResourceWhitespaceNode -Node $childNode -CarriageReturnToken $CarriageReturnToken
        }
    }
}

function ConvertTo-CanonicalResourceXml {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $document = [System.Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $true
    $reader = [System.Xml.XmlReader]::Create($Path)
    try {
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $carriageReturnToken = "__FOUNDRY_XML_CR_$([System.Guid]::NewGuid().ToString('N'))__"
    Format-ResourceWhitespaceNode -Node $document -CarriageReturnToken $carriageReturnToken

    $dataNodes = [System.Collections.Generic.List[System.Xml.XmlElement]]::new()
    foreach ($dataNode in $document.DocumentElement.SelectNodes('data')) {
        foreach ($commentNode in @($dataNode.SelectNodes('comment'))) {
            [void]$dataNode.RemoveChild($commentNode)
        }

        $dataNodes.Add($dataNode)
    }

    $dataNodes.Sort([System.Comparison[System.Xml.XmlElement]] {
            param($left, $right)

            return [System.StringComparer]::Ordinal.Compare(
                $left.GetAttribute('name'),
                $right.GetAttribute('name'))
        })

    foreach ($dataNode in $dataNodes) {
        [void]$document.DocumentElement.RemoveChild($dataNode)
    }

    foreach ($dataNode in $dataNodes) {
        [void]$document.DocumentElement.AppendChild($dataNode)
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $settings.OmitXmlDeclaration = $false

    $stream = [System.IO.MemoryStream]::new()
    try {
        $writer = [System.Xml.XmlWriter]::Create($stream, $settings)
        try {
            $document.Save($writer)
        }
        finally {
            $writer.Dispose()
        }

        $formattedXml = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }

    return $formattedXml.Replace($carriageReturnToken, '&#xD;').TrimEnd("`r", "`n") + "`r`n"
}

$invalidResourceFiles = @()
foreach ($resourceFile in $ResourceFiles) {
    $resourcePath = Join-Path $repoRoot $resourceFile
    $actualBytes = [System.IO.File]::ReadAllBytes($resourcePath)
    $expectedBytes = [System.Text.Encoding]::UTF8.GetBytes(
        (ConvertTo-CanonicalResourceXml -Path $resourcePath))

    if ([System.Convert]::ToBase64String($actualBytes) -cne [System.Convert]::ToBase64String($expectedBytes)) {
        if ($VerifyResourceFormatting) {
            $invalidResourceFiles += $resourceFile
        }
        else {
            [System.IO.File]::WriteAllBytes($resourcePath, $expectedBytes)
            Write-Output "Formatted resource file: $resourceFile"
        }
    }
}

if ($VerifyResourceFormatting) {
    if ($invalidResourceFiles.Count -gt 0) {
        $invalidResourceFiles | ForEach-Object { Write-Output "Resource formatting required: $_" }
        throw 'Resource formatting verification failed. Run scripts\Format-Foundry.ps1.'
    }

    return
}

$xamlFiles = git -C $repoRoot ls-files '*.xaml'
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list tracked XAML files.'
}

dotnet format whitespace $solutionPath --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format whitespace failed.'
}

dotnet format style $solutionPath --diagnostics IDE0073 --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format style IDE0073 failed.'
}

dotnet tool restore --tool-manifest $toolManifestPath
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet tool restore failed.'
}

Push-Location $repoRoot
try {
    if ($xamlFiles.Count -gt 0) {
        dotnet tool run xstyler -- -f ($xamlFiles -join ',') -c .xamlstyler -l Verbose
        if ($LASTEXITCODE -ne 0) {
            throw 'XAML formatting failed.'
        }
    }
}
finally {
    Pop-Location
}
