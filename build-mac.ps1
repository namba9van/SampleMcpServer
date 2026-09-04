$ErrorActionPreference = 'Stop'

# Кросс-публикует self-contained сборку macOS Apple Silicon (arm64) в _Release\mac.
$root = $PSScriptRoot
$project = Join-Path $root 'SampleMcpServer.csproj'
$out = Join-Path $root '_Release\mac'
$rid = 'osx-arm64'

Write-Host "Cleaning macOS release folder: $out" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "Publishing SampleMcpServer for $rid..." -ForegroundColor Cyan
dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid with exit code $LASTEXITCODE." }

$binary = Join-Path $out 'SampleMcpServer'
if (-not (Test-Path $binary)) { throw "macOS executable was not created: $binary" }

Write-Host "macOS arm64 release is ready: $out" -ForegroundColor Green
