<#
.SYNOPSIS
    Benchmark empty-directory scanning and deletion across tools.
.DESCRIPTION
    Generates a synthetic tree (via Generate-Tree.ps1), then times each scanner
    in dry-run / scan-only mode. Results are written to a markdown table on stdout.
.PARAMETER RedExe
    Path to RED+.exe (default: ../../bin/Release/RED+.exe relative to this script).
.PARAMETER TotalDirs
    Number of directories for the benchmark tree (default 10000).
.PARAMETER EmptyPercent
    Percentage of directories that are empty (default 60).
.PARAMETER Runs
    Number of timed runs per tool (default 3, median reported).
#>
param(
    [string]$RedExe,
    [int]$TotalDirs = 10000,
    [int]$EmptyPercent = 60,
    [int]$Runs = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $RedExe) {
    $RedExe = Join-Path $scriptDir "..\..\bin\Release\RED+.exe"
}
$RedExe = (Resolve-Path $RedExe -ErrorAction Stop).Path

$benchRoot = Join-Path $env:TEMP "red_benchmark_tree"

function New-BenchTree {
    if (Test-Path $benchRoot) {
        Remove-Item -Recurse -Force $benchRoot
    }
    & "$scriptDir\Generate-Tree.ps1" -OutPath $benchRoot -TotalDirs $TotalDirs -EmptyPercent $EmptyPercent -Seed 42
}

function Get-Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2 -eq 0) {
        return ($sorted[$n/2 - 1] + $sorted[$n/2]) / 2.0
    }
    return $sorted[([math]::Floor($n/2))]
}

function Measure-Tool {
    param(
        [string]$Name,
        [scriptblock]$Setup,
        [scriptblock]$Action
    )

    $times = @()
    for ($r = 0; $r -lt $Runs; $r++) {
        if ($Setup) { & $Setup }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            & $Action | Out-Null
            $sw.Stop()
            $times += $sw.Elapsed.TotalSeconds
        } catch {
            $sw.Stop()
            $times += -1
        }
    }

    $validTimes = @($times | Where-Object { $_ -ge 0 })
    if ($validTimes.Count -eq 0) {
        return [PSCustomObject]@{ Tool = $Name; MedianSeconds = "N/A"; Status = "Error" }
    }
    $median = Get-Median $validTimes
    return [PSCustomObject]@{
        Tool = $Name
        MedianSeconds = [math]::Round($median, 3)
        Status = "OK"
    }
}

Write-Host "=== RED++ Benchmark ==="
Write-Host "Directories: $TotalDirs ($EmptyPercent% empty)"
Write-Host "Runs per tool: $Runs"
Write-Host "RED+.exe: $RedExe"
Write-Host ""

Write-Host "Generating benchmark tree..."
New-BenchTree

$results = @()

# RED++ standard scan (dry-run)
Write-Host "Benchmarking: RED++ standard scan..."
$results += Measure-Tool -Name "RED++ standard" -Setup $null -Action {
    & $RedExe -silent -path $benchRoot -dryrun -quiet -no-mft
}

# RED++ MFT scan (dry-run, requires admin)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Write-Host "Benchmarking: RED++ MFT scan..."
    $results += Measure-Tool -Name "RED++ MFT" -Setup $null -Action {
        & $RedExe -silent -path $benchRoot -dryrun -quiet -mft
    }
} else {
    Write-Host "Skipping: RED++ MFT scan (requires admin)"
    $results += [PSCustomObject]@{ Tool = "RED++ MFT"; MedianSeconds = "N/A"; Status = "Needs admin" }
}

# PowerShell one-liner
Write-Host "Benchmarking: PowerShell Get-ChildItem..."
$results += Measure-Tool -Name "PowerShell" -Setup $null -Action {
    $count = 0
    Get-ChildItem -Path $benchRoot -Recurse -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $children = @(Get-ChildItem $_.FullName -Force -ErrorAction SilentlyContinue)
        if ($children.Count -eq 0) { $count++ }
    }
    $count
}

# robocopy /S /MOVE trick (scan only via /L)
Write-Host "Benchmarking: robocopy /S /MOVE /L..."
$roboTarget = Join-Path $env:TEMP "red_bench_robocopy_target"
$results += Measure-Tool -Name "robocopy /L" -Setup {
    New-Item -ItemType Directory -Force -Path $roboTarget | Out-Null
} -Action {
    $null = robocopy $benchRoot $roboTarget /S /MOVE /L /NJH /NJS /NFL /NDL 2>$null
}
if (Test-Path $roboTarget) { Remove-Item -Recurse -Force $roboTarget -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "=== Results ==="
Write-Host ""

$hw = Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name
$os = (Get-CimInstance Win32_OperatingSystem).Caption
$ram = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)

Write-Host "**Hardware:** $hw, $($ram) GB RAM"
Write-Host "**OS:** $os"
Write-Host "**Dataset:** $TotalDirs directories ($EmptyPercent% empty), seed 42"
Write-Host "**Methodology:** Median of $Runs runs, dry-run/scan-only (no deletion)"
Write-Host ""
Write-Host "| Tool | Median (s) | Status |"
Write-Host "|------|-----------|--------|"
foreach ($r in $results) {
    Write-Host "| $($r.Tool) | $($r.MedianSeconds) | $($r.Status) |"
}

# Cleanup
Write-Host ""
Write-Host "Cleaning up benchmark tree..."
Remove-Item -Recurse -Force $benchRoot -ErrorAction SilentlyContinue
Write-Host "Done."
