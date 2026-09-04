$ErrorActionPreference = 'Stop'

# Публикует self-contained сборку Windows x64 в _Release\win-x64.
$root = $PSScriptRoot
$project = Join-Path $root 'SampleMcpServer.csproj'
$out = Join-Path $root '_Release\win-x64'
$rid = 'win-x64'

Write-Host "Cleaning Windows release folder: $out" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "Publishing SampleMcpServer for $rid..." -ForegroundColor Cyan
dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid with exit code $LASTEXITCODE." }

$binary = Join-Path $out 'SampleMcpServer.exe'
if (-not (Test-Path $binary)) { throw "Windows executable was not created: $binary" }

Write-Host "Windows x64 release is ready: $out" -ForegroundColor Green
