<#Requires -RunAsAdministrator

.SYNOPSIS
    Installs ALT_ICS as a Windows Service with firewall rules and recovery options.
.DESCRIPTION
    Builds (or uses an existing publish) the ALT_ICS.Service project, registers it
    as a Windows service via sc.exe, configures auto-start and recovery (restart on
    failure), creates required firewall rules, and optionally enables IP forwarding.
.PARAMETER PublishPath
    Path to the already-published service binaries.  If omitted, the script runs
    build.ps1 to produce a fresh publish.
.PARAMETER ServiceName
    The internal name for the service (default: ALT_ICS).
.PARAMETER ServiceDisplayName
    Display name shown in services.msc.
.PARAMETER ServiceDescription
    Description shown in services.msc.
.PARAMETER SkipBuild
    If set, skips the build step even when PublishPath is not specified (fails fast).
.PARAMETER SkipFirewall
    If set, does not create firewall rules.
.PARAMETER SkipIpForward
    If set, does not enable IP forwarding.
.PARAMETER StartService
    If set (default), starts the service after installation.
.EXAMPLE
    .\scripts\install.ps1
.EXAMPLE
    .\scripts\install.ps1 -PublishPath "C:\build\publish" -SkipBuild
#>

[CmdletBinding()]
param(
    [string]$PublishPath = "",
    [string]$ServiceName = "ALT_ICS",
    [string]$ServiceDisplayName = "ALT_ICS — Alternative Internet Connection Sharing",
    [string]$ServiceDescription = "Custom NAT-based internet connection sharing replacing Windows ICS",
    [switch]$SkipBuild,
    [switch]$SkipFirewall,
    [switch]$SkipIpForward,
    [switch]$StartService = $true
)

$ErrorActionPrevention = "Stop"

# ---------------------------------------------------------------------------
# Coloured output helpers
# ---------------------------------------------------------------------------
function Write-Step {
    param([string]$Message)
    Write-Host ">>> " -NoNewline -ForegroundColor Cyan
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
# Helper: run a command block with rollback registration
# ---------------------------------------------------------------------------
$script:rollbackActions = [System.Collections.Generic.Stack[string]]::new()

function Register-Rollback {
    param([scriptblock]$Action)
    $script:rollbackActions.Push($Action)
}

function Invoke-Rollback {
    Write-Host ""
    Write-Host "!!! Rolling back installation ..." -ForegroundColor Red
    while ($script:rollbackActions.Count -gt 0) {
        $action = $script:rollbackActions.Pop()
        try {
            & $action
        }
        catch {
            Write-Warn "Rollback action failed: $_"
        }
    }
}

# ---------------------------------------------------------------------------
# 1. Admin check (redundant given #Requires but explicit is safer)
# ---------------------------------------------------------------------------
Write-Step "Checking administrator privileges ..."
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Fail "This script must be run as Administrator.  Elevate and try again."
    exit 1
}
Write-Success "Running as Administrator"

# ---------------------------------------------------------------------------
# 2. Check .NET 8 runtime
# ---------------------------------------------------------------------------
Write-Step "Checking .NET 8 runtime ..."
try {
    $runtimes = dotnet --list-runtimes 2>$null
    if ($runtimes -match "Microsoft\.NETCore\.App 8\.\d+\.\d+") {
        Write-Success ".NET 8 runtime found"
    }
    else {
        Write-Warn ".NET 8 runtime not detected.  The service may fail to start if 'SelfContained' publish is not used."
        Write-Info "Install from: https://dotnet.microsoft.com/download/dotnet/8.0"
    }
}
catch {
    Write-Warn "Could not check .NET runtime version (dotnet CLI may not be in PATH)."
}

# ---------------------------------------------------------------------------
# 3. Build / resolve publish path
# ---------------------------------------------------------------------------
if (-not $PublishPath) {
    if ($SkipBuild) {
        Write-Fail "PublishPath not specified and -SkipBuild is set.  Provide a valid -PublishPath."
        exit 1
    }
    Write-Step "Building project (this may take a minute) ..."
    $buildScript = Join-Path -Path $PSScriptRoot -ChildPath "build.ps1"
    if (-not (Test-Path -LiteralPath $buildScript)) {
        Write-Fail "Build script not found at: $buildScript"
        exit 1
    }
    try {
        & $buildScript
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 exited with code $LASTEXITCODE" }
    }
    catch {
        Write-Fail "Build step failed: $_"
        exit 1
    }
    # Default publish path after build
    $PublishPath = Join-Path -Path $PSScriptRoot -ChildPath "..\ALT_ICS.Service\bin\Release\net8.0\publish"
}
else {
    Write-Info "Using existing publish path: $PublishPath"
}

# Resolve to a canonical path
$PublishPath = Resolve-Path -LiteralPath $PublishPath -ErrorAction Stop

$exePath = Join-Path -Path $PublishPath -ChildPath "ALT_ICS.Service.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Fail "Executable not found at expected path: $exePath"
    exit 1
}
Write-Success "Service executable: $exePath"

# ---------------------------------------------------------------------------
# 4. Stop / remove any existing service with the same name
# ---------------------------------------------------------------------------
Write-Step "Checking for existing '$ServiceName' service ..."
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Info "Found existing service '$ServiceName' (status: $($existing.Status)).  Removing ..."
    if ($existing.Status -eq 'Running') {
        try {
            Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            Write-Success "Service stopped"
        }
        catch {
            Write-Warn "Could not stop service: $_"
        }
    }
    try {
        & sc.exe delete $ServiceName 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc delete returned $LASTEXITCODE" }
        Write-Success "Existing service deleted"
        Start-Sleep -Seconds 2
    }
    catch {
        Write-Warn "Could not delete existing service (may already be gone): $_"
    }
}

# ---------------------------------------------------------------------------
# 5. Create the service
# ---------------------------------------------------------------------------
Write-Step "Creating Windows Service '$ServiceName' ..."
$binaryPath = "`"$exePath`""

try {
    # Start with the bare minimum to get the service created
    & sc.exe create $ServiceName `
        binPath= $binaryPath `
        start= auto `
        DisplayName= "`"$ServiceDisplayName`"" 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) { throw "sc create returned $LASTEXITCODE" }

    # Set description
    & sc.exe description $ServiceName "`"$ServiceDescription`"" 2>&1 | Out-Null

    Write-Success "Service '$ServiceName' created"

    # Register rollback: remove service on failure
    Register-Rollback -Action {
        & sc.exe delete $ServiceName 2>&1 | Out-Null
        Write-Info "Rollback: service '$ServiceName' deleted"
    }
}
catch {
    Write-Fail "Failed to create service: $_"
    Invoke-Rollback
    exit 1
}

# ---------------------------------------------------------------------------
# 6. Configure recovery options (restart on failure)
# ---------------------------------------------------------------------------
Write-Step "Configuring service recovery options ..."
try {
    # sc failure sets actions for first / second / subsequent failures
    & sc.exe failure $ServiceName `
        reset= 86400 `
        actions= restart/60000/restart/120000/restart/300000 2>&1 | Out-Null

    # Also set failure flag on the service so that the OS honours these
    & sc.exe failureflag $ServiceName 1 2>&1 | Out-Null

    Write-Success "Recovery configured: restart on failure"
}
catch {
    Write-Warn "Could not set recovery options: $_"
}

# ---------------------------------------------------------------------------
# 7. Verify service registration & binary path
# ---------------------------------------------------------------------------
$verify = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $verify) {
    Write-Fail "Service verification failed – service not found after creation."
    Invoke-Rollback
    exit 1
}
Write-Success "Service verified in service control manager"

# ---------------------------------------------------------------------------
# 8. Firewall rules
# ---------------------------------------------------------------------------
if (-not $SkipFirewall) {
    Write-Step "Configuring Windows Firewall rules ..."
    $fwScript = Join-Path -Path $PSScriptRoot -ChildPath "setup-firewall.ps1"
    if (Test-Path -LiteralPath $fwScript) {
        try {
            & $fwScript
            if ($LASTEXITCODE -ne 0) { throw "setup-firewall.ps1 exited with code $LASTEXITCODE" }
            Write-Success "Firewall rules configured"
            Register-Rollback -Action {
                & $fwScript -RemoveOnly 2>&1 | Out-Null
                Write-Info "Rollback: firewall rules removed"
            }
        }
        catch {
            Write-Warn "Firewall configuration failed: $_"
        }
    }
    else {
        Write-Warn "Firewall script not found at: $fwScript – skipping"
    }
}
else {
    Write-Info "Firewall configuration skipped (-SkipFirewall)"
}

# ---------------------------------------------------------------------------
# 9. Enable IP forwarding
# ---------------------------------------------------------------------------
if (-not $SkipIpForward) {
    Write-Step "Enabling IP forwarding (IPEnableRouter) ..."
    try {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"
        $current = Get-ItemProperty -Path $regPath -Name IPEnableRouter -ErrorAction SilentlyContinue
        if ($current.IPEnableRouter -ne 1) {
            Set-ItemProperty -Path $regPath -Name IPEnableRouter -Value 1 -Type DWord -Force -ErrorAction Stop
            Write-Success "IPEnableRouter set to 1 (reboot may be required)"
            Register-Rollback -Action {
                Set-ItemProperty -Path $regPath -Name IPEnableRouter -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue
                Write-Info "Rollback: IPEnableRouter restored to 0"
            }
        }
        else {
            Write-Info "IP forwarding already enabled"
        }
    }
    catch {
        Write-Warn "Could not enable IP forwarding: $_"
    }

    # Also enable the Routing and Remote Access service (optional, but helpful)
    try {
        Set-Service -Name RemoteAccess -StartupType Manual -ErrorAction SilentlyContinue
    }
    catch {
        # non-critical
    }
}
else {
    Write-Info "IP forwarding skipped (-SkipIpForward)"
}

# ---------------------------------------------------------------------------
# 10. Start the service
# ---------------------------------------------------------------------------
if ($StartService) {
    Write-Step "Starting service '$ServiceName' ..."
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        Start-Sleep -Seconds 3

        $svc = Get-Service -Name $ServiceName -ErrorAction Stop
        if ($svc.Status -eq 'Running') {
            Write-Success "Service '$ServiceName' is now Running"
            Register-Rollback -Action {
                Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
                Write-Info "Rollback: service stopped"
            }
        }
        else {
            Write-Warn "Service status after start attempt: $($svc.Status)"
        }
    }
    catch {
        Write-Warn "Could not start service: $_"
        Write-Info "Check the Event Log (source 'ALT_ICS') for details."
    }
}
else {
    Write-Info "Service start skipped (-StartService:`$false)"
}

# ---------------------------------------------------------------------------
# 11. Final summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║           ALT_ICS INSTALLATION COMPLETE                 ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Service name   : $ServiceName"
Write-Host "  Display name   : $ServiceDisplayName"
Write-Host "  Executable     : $exePath"
Write-Host "  Publish path   : $PublishPath"
Write-Host "  Auto-start     : Yes"
Write-Host "  Recovery       : Restart on failure"
$fwStatus = if ($SkipFirewall) { "Skipped" } else { "Configured" }
Write-Host "  Firewall       : $fwStatus"
$ipStatus = if ($SkipIpForward) { "Skipped" } else { "Enabled (IPEnableRouter=1)" }
Write-Host "  IP forwarding  : $ipStatus"
$startStatus = if ($StartService) { (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status } else { "Not started" }
Write-Host "  Service status : $startStatus"
Write-Host ""
Write-Host "  Manage via: services.msc  or  sc.exe query $ServiceName"
Write-Host "  Uninstall : .\scripts\uninstall.ps1"
Write-Host ""
