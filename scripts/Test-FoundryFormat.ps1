$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'src\Foundry.slnx'
$toolManifestPath = Join-Path $repoRoot '.config\dotnet-tools.json'

function Get-ChangedResourceFile {
    $resourceFiles = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    $workingTreeFiles = git -C $repoRoot diff --name-only --diff-filter=ACMR HEAD -- '*.resw' '*.resx'
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to list locally changed resource files.'
    }

    foreach ($resourceFile in $workingTreeFiles) {
        [void]$resourceFiles.Add($resourceFile)
    }

    $baseBranch = if ($env:GITHUB_BASE_REF) { $env:GITHUB_BASE_REF } else { 'main' }
    $baseReference = "refs/remotes/origin/$baseBranch"
    git -C $repoRoot show-ref --verify --quiet $baseReference
    if ($LASTEXITCODE -ne 0 -and $env:GITHUB_BASE_REF) {
        git -C $repoRoot fetch --no-tags --depth=1 origin "$($baseBranch):$baseReference"
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to fetch base branch '$baseBranch'."
        }
    }

    git -C $repoRoot show-ref --verify --quiet $baseReference
    if ($LASTEXITCODE -eq 0) {
        $mergeBase = git -C $repoRoot merge-base HEAD $baseReference
        if ($LASTEXITCODE -ne 0 -and $env:GITHUB_BASE_REF) {
            $isShallowRepository = git -C $repoRoot rev-parse --is-shallow-repository
            if ($LASTEXITCODE -ne 0) {
                throw 'Failed to determine whether the repository is shallow.'
            }

            if ($isShallowRepository -eq 'true') {
                git -C $repoRoot fetch --no-tags --unshallow origin
            }
            else {
                git -C $repoRoot fetch --no-tags origin $baseBranch
            }

            if ($LASTEXITCODE -ne 0) {
                throw "Failed to fetch history for base branch '$baseBranch'."
            }

            $mergeBase = git -C $repoRoot merge-base HEAD $baseReference
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to determine the merge base with '$baseReference'."
        }

        $branchFiles = git -C $repoRoot diff --name-only --diff-filter=ACMR "$mergeBase..HEAD" -- '*.resw' '*.resx'
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to list resource files changed on the current branch.'
        }

        foreach ($resourceFile in $branchFiles) {
            [void]$resourceFiles.Add($resourceFile)
        }
    }

    return @($resourceFiles | Sort-Object)
}

$resourceFiles = Get-ChangedResourceFile
if ($resourceFiles.Count -gt 0) {
    & (Join-Path $PSScriptRoot 'Format-Foundry.ps1') -VerifyResourceFormatting -ResourceFiles $resourceFiles
}

$xamlFiles = git -C $repoRoot ls-files '*.xaml'
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list tracked XAML files.'
}

dotnet format whitespace $solutionPath --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format whitespace verification failed. Run scripts\Format-Foundry.ps1.'
}

dotnet format style $solutionPath --diagnostics IDE0073 --verify-no-changes --no-restore --verbosity diagnostic
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet format style IDE0073 verification failed. Run scripts\Format-Foundry.ps1.'
}

dotnet tool restore --tool-manifest $toolManifestPath
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet tool restore failed.'
}

Push-Location $repoRoot
try {
    if ($xamlFiles.Count -gt 0) {
        dotnet tool run xstyler -- -f ($xamlFiles -join ',') -p -c .xamlstyler -l Verbose
        if ($LASTEXITCODE -ne 0) {
            throw 'XAML formatting verification failed. Run scripts\Format-Foundry.ps1.'
        }
    }
}
finally {
    Pop-Location
}
