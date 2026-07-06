<#Requires -RunAsAdministrator

.SYNOPSIS
    Full deployment of ALT_ICS: build, install, and configure in one command.
.DESCRIPTION
    Orchestrates the complete deployment workflow:
      1. Build the solution (Release)
      2. Install the Windows Service (auto-start, recovery on failure)
      3. Configure Windows Firewall rules
      4. Enable IP forwarding
      5. Start the service
      6. Run a quick health verification
.PARAMETER ServiceName
    Internal service name (default: ALT_ICS).
.PARAMETER ServiceDisplayName
    Display name in services.msc.
.PARAMETER ServiceDescription
    Service description.
.PARAMETER SkipFirewall
    Skip firewall rule creation.
.PARAMETER SkipIpForward
    Skip enabling IP forwarding.
.PARAMETER PublishPath
    Optional path to pre-built binaries (skips build step).
.PARAMETER NoBuild
    Skip the build step (use with -PublishPath).
.PARAMETER NoStart
    Do not start the service after installation.
.EXAMPLE
    .\scripts\deploy.ps1
.EXAMPLE
    .\scripts\deploy.ps1 -PublishPath "C:\my-build\publish" -NoBuild
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "ALT_ICS",
    [string]$ServiceDisplayName = "ALT_ICS - Alternative Internet Connection Sharing",
    [string]$ServiceDescription = "Custom NAT-based internet connection sharing replacing Windows ICS",
    [switch]$SkipFirewall,
    [switch]$SkipIpForward,
    [string]$PublishPath = "",
    [switch]$NoBuild,
    [switch]$NoStart
)

$ErrorActionPrevention = "Stop"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step {
    param([string]$Message)
    Write-Host "`n>>> " -NoNewline -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "[  OK]" -NoNewline -ForegroundColor Green
    Write-Host " $Message"
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN]" -NoNewline -ForegroundColor Yellow
    Write-Host " $Message"
}

function Write-Fail {
    param([string]$Message)
    Write-Host "[FAIL]" -NoNewline -ForegroundColor Red
    Write-Host " $Message"
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO]" -NoNewline -ForegroundColor Gray
    Write-Host " $Message"
}

# ---------------------------------------------------------------------------
# 1. Admin check
# ---------------------------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Fail "This script must be run as Administrator."
    exit 1
}

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║         ALT_ICS - Full Deployment             ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════╝" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 2. Build step
# ---------------------------------------------------------------------------
if ($NoBuild -and (-not $PublishPath)) {
    Write-Fail "-NoBuild requires a -PublishPath.  Provide the path to pre-built binaries."
    exit 1
}

if ($PublishPath) {
    # Resolve and verify the custom publish path
    Write-Step "Using pre-built binaries from: $PublishPath"
    try {
        $PublishPath = Resolve-Path -LiteralPath $PublishPath -ErrorAction Stop
    }
    catch {
        Write-Fail "Publish path not found: $PublishPath"
        exit 1
    }
    $exeCheck = Join-Path -Path $PublishPath -ChildPath "ALT_ICS.Service.exe"
    if (-not (Test-Path -LiteralPath $exeCheck)) {
        Write-Fail "ALT_ICS.Service.exe not found in: $PublishPath"
        exit 1
    }
    Write-Success "Executable verified"
}
else {
    Write-Step "Building solution (Release) ..."
    $buildScript = Join-Path -Path $PSScriptRoot -ChildPath "build.ps1"
    if (-not (Test-Path -LiteralPath $buildScript)) {
        Write-Fail "Build script not found: $buildScript"
        exit 1
    }
    try {
        & $buildScript
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 exited with code $LASTEXITCODE" }
    }
    catch {
        Write-Fail "Build failed: $_"
        exit 1
    }
    $PublishPath = Join-Path -Path $PSScriptRoot -ChildPath "..\ALT_ICS.Service\bin\Release\net8.0\publish"
    Write-Success "Build completed"
}

# ---------------------------------------------------------------------------
# 3. Install service
# ---------------------------------------------------------------------------
Write-Step "Installing Windows Service ..."
$installScript = Join-Path -Path $PSScriptRoot -ChildPath "install.tmp.ps1"
if (-not (Test-Path -LiteralPath $installScript)) {
    Write-Fail "Install script not found: $installScript"
    exit 1
}

try {
    Write-Info "PublishPath   = [$PublishPath]"
    Write-Info "ServiceName   = [$ServiceName]"
    Write-Info "DisplayName   = [$ServiceDisplayName]"
    Write-Info "Description   = [$ServiceDescription]"

    & $installScript -PublishPath $PublishPath -ServiceName $ServiceName -ServiceDisplayName $ServiceDisplayName -ServiceDescription $ServiceDescription
    if ($LASTEXITCODE -ne 0) { throw "install.ps1 exited with code $LASTEXITCODE" }
    Write-Success "Service installed"
}
catch {
    Write-Fail "Installation step failed: $_"
    exit 1
}

# ---------------------------------------------------------------------------
# 4. Quick health verification
# ---------------------------------------------------------------------------
if (-not $NoStart) {
    Write-Step "Verifying service is running ..."
    Start-Sleep -Seconds 4
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Write-Success "Service '$ServiceName' is Running"

        # Try the health endpoint (non-fatal if it fails)
        try {
            $healthUrl = "http://localhost:51001/health"
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "Health endpoint responded: HTTP $($response.StatusCode)"
            }
            else {
                Write-Info "Health endpoint returned HTTP $($response.StatusCode)"
            }
        }
        catch {
            Write-Warn "Health endpoint not reachable yet (service may still be initialising): $_"
        }
    }
    else {
        $status = if ($svc) { $svc.Status } else { "Not found" }
        Write-Warn "Service status: $status"
        Write-Info "Check 'services.msc' or the Event Log for details."
    }
}

# ---------------------------------------------------------------------------
# 5. Final deployment summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║           ALT_ICS DEPLOYMENT COMPLETE                   ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

$serviceStatus = if ($NoStart) { "Not started (user requested)" } else { (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status }

Write-Host "  Service        : $ServiceName"
Write-Host "  Status         : $serviceStatus"
Write-Host "  Binary path    : $PublishPath\ALT_ICS.Service.exe"
Write-Host "  Firewall       : $(if ($SkipFirewall) { 'Skipped' } else { 'Configured' })"
Write-Host "  IP forwarding  : $(if ($SkipIpForward) { 'Skipped' } else { 'Enabled' })"
Write-Host ""
Write-Host "  Ports opened:"
Write-Host "    53/UDP   - DNS relay"
Write-Host "    67/UDP   - DHCP server"
Write-Host "    51000/TCP - SignalR control channel"
Write-Host "    51001/TCP - Health endpoint"
Write-Host ""
Write-Host "  Manage : sc.exe query $ServiceName"
Write-Host "  Stop   : sc.exe stop $ServiceName"
Write-Host "  Start  : sc.exe start $ServiceName"
Write-Host "  Logs   : Event Viewer -> Windows Logs -> Application (source: ALT_ICS)"
Write-Host "  Remove : .\scripts\uninstall.ps1"
Write-Host ""

