<#
.SYNOPSIS
    Generate a synthetic directory tree for benchmarking empty-directory scanners.
.DESCRIPTION
    Creates a configurable mix of empty and non-empty directories at varying depths.
    The same seed always produces the same tree for reproducible benchmarks.
.PARAMETER OutPath
    Root directory for the generated tree. Created if missing; fails if non-empty.
.PARAMETER TotalDirs
    Total number of directories to create (default 10000).
.PARAMETER EmptyPercent
    Percentage of directories that are empty (default 60).
.PARAMETER MaxDepth
    Maximum nesting depth (default 8).
.PARAMETER Seed
    Random seed for reproducibility (default 42).
#>
param(
    [Parameter(Mandatory)]
    [string]$OutPath,

    [int]$TotalDirs = 10000,
    [int]$EmptyPercent = 60,
    [int]$MaxDepth = 8,
    [int]$Seed = 42
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (Test-Path $OutPath) {
    $existing = Get-ChildItem $OutPath -Force -ErrorAction SilentlyContinue
    if ($existing.Count -gt 0) {
        Write-Error "Output path '$OutPath' is not empty. Remove it first."
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $OutPath | Out-Null
$root = (Resolve-Path $OutPath).Path

$rng = New-Object System.Random($Seed)
$emptyCount = [math]::Floor($TotalDirs * $EmptyPercent / 100)
$nonEmptyCount = $TotalDirs - $emptyCount

$dirs = [System.Collections.Generic.List[string]]::new()
$dirs.Add($root)

function New-RandomName {
    $len = $rng.Next(3, 12)
    $chars = 'abcdefghijklmnopqrstuvwxyz0123456789_-'
    $name = ''
    for ($j = 0; $j -lt $len; $j++) {
        $name += $chars[$rng.Next($chars.Length)]
    }
    return $name
}

$created = 0
while ($created -lt $TotalDirs) {
    $parentIdx = $rng.Next($dirs.Count)
    $parent = $dirs[$parentIdx]

    $depth = ($parent.Substring($root.Length).Split('\', [StringSplitOptions]::RemoveEmptyEntries)).Count
    if ($depth -ge $MaxDepth) { continue }

    $name = New-RandomName
    $path = Join-Path $parent $name
    if (Test-Path $path) { continue }

    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $dirs.Add($path)
    $created++

    if ($created % 1000 -eq 0) {
        Write-Host "  Created $created / $TotalDirs directories..."
    }
}

$emptyDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$indices = 1..($dirs.Count - 1) | Sort-Object { $rng.Next() } | Select-Object -First $emptyCount
foreach ($idx in $indices) {
    $emptyDirs.Add($dirs[$idx]) | Out-Null
}

$fileCount = 0
for ($i = 1; $i -lt $dirs.Count; $i++) {
    if ($emptyDirs.Contains($dirs[$i])) { continue }
    $filePath = Join-Path $dirs[$i] "content.txt"
    if (-not (Test-Path $filePath)) {
        [IO.File]::WriteAllText($filePath, "benchmark data")
        $fileCount++
    }
}

Write-Host ""
Write-Host "Tree generated at: $root"
Write-Host "  Total directories: $($dirs.Count - 1)"
Write-Host "  Empty directories: $emptyCount"
Write-Host "  Non-empty directories: $nonEmptyCount"
Write-Host "  Files created: $fileCount"
Write-Host "  Max depth: $MaxDepth"
Write-Host "  Seed: $Seed"
