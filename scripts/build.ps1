<#
.SYNOPSIS
    Builds the ALT_ICS solution and publishes the Windows Service.
.DESCRIPTION
    Runs dotnet restore, build (Release), and publish (Release) for the
    ALT_ICS.Service project.  The service is published as a single-file,
    ReadyToRun (R2R) executable targeting net8.0.
.PARAMETER Configuration
    Build configuration (default: Release).
.PARAMETER SolutionPath
    Path to the .sln file (auto-detected from script location).
.PARAMETER OutputPath
    Override the default publish output directory.
.EXAMPLE
    .\scripts\build.ps1
.EXAMPLE
    .\scripts\build.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$SolutionPath = "$PSScriptRoot\..\ALT_ICS.sln",
    [string]$OutputPath = ""
)

$ErrorActionPrevention = "Stop"

# Resolve paths to absolute
$SolutionPath = Resolve-Path -LiteralPath $SolutionPath -ErrorAction Stop
$RepoRoot = Split-Path -Parent $SolutionPath

function Write-Status {
    param([string]$Message, [string]$Color = "Cyan")
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host "BUILD" -NoNewline -ForegroundColor $Color
    Write-Host "] $Message" -ForegroundColor Gray
}

function Write-Success {
    param([string]$Message)
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host "  OK" -NoNewline -ForegroundColor Green
    Write-Host "] $Message" -ForegroundColor Gray
}

function Write-Error {
    param([string]$Message)
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host "FAIL" -NoNewline -ForegroundColor Red
    Write-Host "] $Message" -ForegroundColor Gray
}

# ---- Check prerequisites ----
try {
    $dotnet = Get-Command "dotnet" -ErrorAction Stop
    Write-Status "Found dotnet at $($dotnet.Source)"
}
catch {
    Write-Error "dotnet CLI not found.  Ensure the .NET 8 SDK is installed."
    exit 1
}

# Verify the solution file exists
if (-not (Test-Path -LiteralPath $SolutionPath)) {
    Write-Error "Solution file not found at: $SolutionPath"
    exit 1
}

# ---- Restore ----
Write-Status "Restoring NuGet packages..."
$restoreArgs = @(
    "restore",
    "`"$SolutionPath`""
)
try {
    & $dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    Write-Success "NuGet packages restored"
}
catch {
    Write-Error "Restore failed: $_"
    exit 1
}

# ---- Build ----
Write-Status "Building solution ($Configuration)..."
$buildArgs = @(
    "build",
    "`"$SolutionPath`"",
    "-c", $Configuration,
    "--no-restore"
)
try {
    & $dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
    Write-Success "Build completed"
}
catch {
    Write-Error "Build failed: $_"
    exit 1
}

# ---- Publish ----
$serviceProject = Join-Path -Path $RepoRoot -ChildPath "ALT_ICS.Service\ALT_ICS.Service.csproj"
if (-not (Test-Path -LiteralPath $serviceProject)) {
    Write-Error "Service project not found at: $serviceProject"
    exit 1
}

if (-not $OutputPath) {
    $OutputPath = Join-Path -Path $RepoRoot -ChildPath "ALT_ICS.Service\bin\$Configuration\net8.0\publish"
}

Write-Status "Publishing service to: $OutputPath"

# Restore with RID so PublishReadyToRun assets resolve correctly
& $dotnet restore "`"$serviceProject`"" -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet restore (win-x64) failed with exit code $LASTEXITCODE" }

$publishArgs = @(
    "publish",
    "`"$serviceProject`"",
    "-c", $Configuration,
    "-r", "win-x64",
    "-o", "`"$OutputPath`""
)

# Add single-file / R2R flags from csproj (they are already set in the csproj,
# but we pass them explicitly for clarity / CI scenarios).
$publishArgs += "--self-contained", "false"

try {
    & $dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    Write-Success "Service published to: $OutputPath"

    # Verify the executable was created
    $exePath = Join-Path -Path $OutputPath -ChildPath "ALT_ICS.Service.exe"
    if (Test-Path -LiteralPath $exePath) {
        Write-Success "Executable verified: $exePath"
    }
    else {
        Write-Error "Expected executable not found after publish: $exePath"
        exit 1
    }
}
catch {
    Write-Error "Publish failed: $_"
    exit 1
}

# ---- Summary ----
Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host " BUILD COMPLETE" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Configuration : $Configuration"
Write-Host "  Solution      : $SolutionPath"
Write-Host "  Publish path  : $OutputPath"
Write-Host "  Executable    : $exePath"
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
