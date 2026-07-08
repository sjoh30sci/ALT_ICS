<#
.SYNOPSIS
    Builds the ALT_ICS installer using Inno Setup.
.DESCRIPTION
    Builds the project in Release mode, then compiles the Inno Setup script.
#>

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $PSCommandPath
$ProjectRoot = Resolve-Path "$ScriptDir\.."

# Check for Inno Setup
$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if (-not $iscc) {
    Write-Host "[FAIL] Inno Setup not found." -ForegroundColor Red
    Write-Host "Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

Write-Host "[ OK] Found Inno Setup at: $iscc" -ForegroundColor Green

# Build the project
if (-not $SkipBuild) {
    Write-Host "[BUILD] Building solution (Release)..." -ForegroundColor Cyan
    Push-Location $ProjectRoot
    dotnet publish ALT_ICS.Service\ALT_ICS.Service.csproj -c Release --no-self-contained
    dotnet build ALT_ICS.GUI\ALT_ICS.GUI.csproj -c Release
    Pop-Location
}

# Compile the installer
Write-Host "[BUILD] Compiling installer..." -ForegroundColor Cyan
Push-Location $ScriptDir
& $iscc "ALT_ICS_Setup.iss"
Pop-Location

Write-Host "[ OK] Installer created!" -ForegroundColor Green
