$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$exePath = Join-Path $toolsDir 'RED+.exe'
if (Test-Path $exePath) {
    Remove-Item $exePath -Force -ErrorAction SilentlyContinue
}

$cfgPath = Join-Path $toolsDir 'RED+.cfg'
if (Test-Path $cfgPath) {
    Remove-Item $cfgPath -Force -ErrorAction SilentlyContinue
}

$shimPath = Join-Path $toolsDir 'RED+.exe.ignore'
if (Test-Path $shimPath) {
    Remove-Item $shimPath -Force -ErrorAction SilentlyContinue
}
