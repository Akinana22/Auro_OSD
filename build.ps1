# Auro_OSD build script
# Produces a portable, self-contained single-file exe under .\dist
#
# Usage:
#   .\build.ps1
#   .\build.ps1 -Runtime win-x64 -Configuration Release
#
# If you use the portable SDK (not on PATH), set DOTNET_ROOT first, e.g.:
#   $env:DOTNET_ROOT = "C:\path\to\dotnet8"

param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\Auro_OSD\Auro_OSD.csproj"
$output = Join-Path $root "dist"

$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT "dotnet.exe" } else { "dotnet" }

if (-not (Test-Path $dotnet) -and $dotnet -ne "dotnet") {
    throw "dotnet not found at '$dotnet'. Set DOTNET_ROOT or install the .NET 8 SDK."
}

Write-Host "Publishing Auro_OSD ($Configuration / $Runtime)..." -ForegroundColor Cyan
& $dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $output

if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

Write-Host "Done: $output\Auro_OSD.exe" -ForegroundColor Green
