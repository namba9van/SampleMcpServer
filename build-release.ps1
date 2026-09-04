$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root '_Release'

Write-Host "Cleaning release folder: $out" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "Building SampleMcpServer (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $root 'SampleMcpServer.csproj') -c Release

$serverExe = Join-Path $out 'SampleMcpServer.exe'

if (-not (Test-Path $serverExe)) { throw "SampleMcpServer.exe не найден: $serverExe" }

Write-Host "Release bundle is ready:" -ForegroundColor Green
Write-Host "  $serverExe"
Write-Host ""
Write-Host "Для LM Studio переносите ВСЮ папку _Release целиком." -ForegroundColor Yellow
Write-Host "Ожидаемая структура: _Release\SampleMcpServer.exe" -ForegroundColor Yellow
