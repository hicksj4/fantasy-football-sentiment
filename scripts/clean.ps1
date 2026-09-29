param(
    [switch]$Full,
    [switch]$RemoveObsoleteMlAssets,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $combined = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $combined)) {
        return $null
    }

    return Resolve-Path -LiteralPath $combined
}

function Remove-RepoItem {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $resolved = Resolve-RepoPath $RelativePath
    if ($null -eq $resolved) {
        return
    }

    $target = $resolved.Path
    if (-not $target.StartsWith($repoRoot.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside repository: $target"
    }

    if ($WhatIf) {
        Write-Host "Would remove $RelativePath"
        return
    }

    Write-Host "Removing $RelativePath"
    try {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    catch {
        Write-Warning "Could not fully remove $RelativePath. Close any app using it and rerun cleanup. $($_.Exception.Message)"
    }
}

function Clear-Artifacts {
    $artifacts = Resolve-RepoPath "artifacts"
    if ($null -eq $artifacts) {
        return
    }

    $benchmark = Join-Path $artifacts.Path "benchmark"
    $canonicalReports = @(
        "sentiment-benchmark.md",
        "sentiment-benchmark.json",
        "sentiment-evaluation-b.md",
        "sentiment-evaluation-b.json",
        "realistic-snapshot-summary.md",
        "realistic-snapshot-summary.json"
    )

    foreach ($item in Get-ChildItem -LiteralPath $artifacts.Path -Force) {
        if ($item.FullName -eq $benchmark) {
            continue
        }

        if ($WhatIf) {
            Write-Host "Would remove artifacts/$($item.Name)"
            continue
        }

        Write-Host "Removing artifacts/$($item.Name)"
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }

    if (Test-Path -LiteralPath $benchmark) {
        foreach ($item in Get-ChildItem -LiteralPath $benchmark -Force) {
            if ($canonicalReports -contains $item.Name) {
                continue
            }

            if ($WhatIf) {
                Write-Host "Would remove artifacts/benchmark/$($item.Name)"
                continue
            }

            Write-Host "Removing artifacts/benchmark/$($item.Name)"
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
        }
    }
}

$defaultTargets = @(
    "backend/FantasySentiment.Api/bin",
    "backend/FantasySentiment.Api/obj",
    "backend/FantasySentiment.Tests/bin",
    "backend/FantasySentiment.Tests/obj",
    "frontend/.next",
    "frontend/tsconfig.tsbuildinfo",
    ".tmp-npm-cache",
    ".tmp-nuget-home",
    ".tmp-nuget-packages",
    ".tmp-nuget-source",
    ".vs"
)

foreach ($target in $defaultTargets) {
    Remove-RepoItem $target
}

Clear-Artifacts

if ($Full) {
    Remove-RepoItem "frontend/node_modules"
}

if ($RemoveObsoleteMlAssets) {
    Remove-RepoItem "tools/sentiment-model"
    Remove-RepoItem "backend/FantasySentiment.Api/Models"

    $tools = Resolve-RepoPath "tools"
    if ($null -ne $tools -and @(Get-ChildItem -LiteralPath $tools.Path -Force).Count -eq 0) {
        Remove-RepoItem "tools"
    }
}

Write-Host "Cleanup complete."
