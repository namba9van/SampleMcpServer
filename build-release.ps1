$ErrorActionPreference = 'Stop'

# Собирает все поддерживаемые release-цели из одного сеанса PowerShell на Windows.
# Структура результата:
#   _Release\win-x64  -> Windows x64 (win-x64)
#   _Release\mac      -> macOS Apple Silicon (osx-arm64)
#   _Release\linux    -> Linux x64 (linux-x64)
$root = $PSScriptRoot
$releaseRoot = Join-Path $root '_Release'

Write-Host "Cleaning release root: $releaseRoot" -ForegroundColor Cyan
if (Test-Path $releaseRoot) { Remove-Item $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$buildScripts = @(
    'build-win.ps1',
    'build-mac.ps1',
    'build-linux.ps1'
)

foreach ($script in $buildScripts) {
    Write-Host "" 
    Write-Host "Running $script..." -ForegroundColor Cyan
    & (Join-Path $root $script)
}

Write-Host ""
Write-Host "All release bundles are ready:" -ForegroundColor Green
Write-Host "  $releaseRoot\win-x64"
Write-Host "  $releaseRoot\mac"
Write-Host "  $releaseRoot\linux"
