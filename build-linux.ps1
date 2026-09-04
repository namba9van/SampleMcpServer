$ErrorActionPreference = 'Stop'

# Кросс-публикует self-contained сборку Linux x64 в _Release\linux.
$root = $PSScriptRoot
$project = Join-Path $root 'SampleMcpServer.csproj'
$out = Join-Path $root '_Release\linux'
$rid = 'linux-x64'

Write-Host "Cleaning Linux release folder: $out" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "Publishing SampleMcpServer for $rid..." -ForegroundColor Cyan
dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid with exit code $LASTEXITCODE." }

$binary = Join-Path $out 'SampleMcpServer'
if (-not (Test-Path $binary)) { throw "Linux executable was not created: $binary" }

Write-Host "Linux x64 release is ready: $out" -ForegroundColor Green
