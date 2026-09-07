param(
    [Parameter(Mandatory)][string]$Version,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^(?<year>\d{2})\.(?<month>[1-9]|1[0-2])\.(?<day>[1-9]|[12]\d|3[01])\.(?<build>[1-9]\d*)$') {
    throw 'Version must use YY.M.D.Build without leading zeroes in month, day or build.'
}
$parts = $Matches.Clone()
[void][datetime]::new(2000 + [int]$parts.year, [int]$parts.month, [int]$parts.day)
if ([long]$parts.build -gt 65534) { throw 'Build must fit the assembly version revision (1-65534).' }

$path = Join-Path ([IO.Path]::GetFullPath($RepositoryRoot)) 'src/Directory.Build.props'
$content = [IO.File]::ReadAllText($path)
[xml]$document = $content
foreach ($property in @('Version', 'AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    if (@($document.SelectNodes("/Project/PropertyGroup/$property")).Count -ne 1) {
        throw "Expected exactly one $property in Directory.Build.props."
    }
    $pattern = "(?<=<$property>)[^<]+(?=</$property>)"
    if ([regex]::Matches($content, $pattern).Count -ne 1) { throw "Ambiguous $property element." }
    $content = [regex]::Replace($content, $pattern, $Version)
}
[IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
