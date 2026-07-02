<#Requires -RunAsAdministrator

.SYNOPSIS
    Removes the ALT_ICS Windows Service, firewall rules, and optionally published files.
.DESCRIPTION
    Stops the service if it is running, deletes it via sc.exe, removes the Windows
    Firewall rules that were created during installation, and can optionally clean up
    the published binaries.
.PARAMETER ServiceName
    The internal service name (default: ALT_ICS).
.PARAMETER RemovePublish
    If set, also deletes the publish directory containing the service binaries.
.PARAMETER PublishPath
    Path to the publish directory to remove (used with -RemovePublish).
.PARAMETER Force
    Suppress confirmation prompts.
.EXAMPLE
    .\scripts\uninstall.ps1
.EXAMPLE
    .\scripts\uninstall.ps1 -RemovePublish -Force
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "ALT_ICS",
    [switch]$RemovePublish,
    [string]$PublishPath = "$PSScriptRoot\..\ALT_ICS.Service\bin\Release\net8.0\publish",
    [switch]$Force
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
# 1. Admin check
# ---------------------------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Fail "This script must be run as Administrator."
    exit 1
}

# ---------------------------------------------------------------------------
# 2. Confirmation (unless -Force)
# ---------------------------------------------------------------------------
if (-not $Force) {
    Write-Warn "This will remove the '$ServiceName' service and its firewall rules."
    $confirmation = Read-Host "Are you sure you want to proceed? (y/N) "
    if ($confirmation -notin @('y', 'Y', 'yes', 'YES')) {
        Write-Info "Uninstall cancelled by user."
        exit 0
    }
}

# ---------------------------------------------------------------------------
# 3. Stop & remove the service
# ---------------------------------------------------------------------------
Write-Step "Stopping service '$ServiceName' (if running) ..."
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq 'Running') {
        try {
            Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-Success "Service stopped"
        }
        catch {
            Write-Warn "Could not stop service: $_"
        }
    }

    Write-Step "Deleting service '$ServiceName' ..."
    try {
        & sc.exe delete $ServiceName 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Service deleted"
        }
        else {
            # 1060 = service does not exist
            if ($LASTEXITCODE -ne 1060) {
                throw "sc delete returned exit code $LASTEXITCODE"
            }
            Write-Info "Service already removed"
        }
    }
    catch {
        Write-Fail "Failed to delete service: $_"
        # Continue anyway – the service may be partially removed
    }
}
else {
    Write-Info "Service '$ServiceName' is not installed – nothing to remove"
}

# ---------------------------------------------------------------------------
# 4. Remove firewall rules
# ---------------------------------------------------------------------------
Write-Step "Removing ALT_ICS firewall rules ..."
$fwScript = Join-Path -Path $PSScriptRoot -ChildPath "setup-firewall.ps1"
if (Test-Path -LiteralPath $fwScript) {
    try {
        & $fwScript -RemoveOnly
        Write-Success "Firewall rules removed"
    }
    catch {
        Write-Warn "Firewall cleanup failed: $_"
    }
}
else {
    Write-Info "Firewall script not found – removing rules directly ..."
    $ruleNames = @(
        "ALT_ICS - DNS",
        "ALT_ICS - DHCP",
        "ALT_ICS - SignalR",
        "ALT_ICS - Health"
    )
    foreach ($name in $ruleNames) {
        $rule = Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
        if ($rule) {
            try {
                $rule | Remove-NetFirewallRule -ErrorAction Stop
                Write-Success "Removed rule '$name'"
            }
            catch {
                Write-Warn "Could not remove rule '$name': $_"
            }
        }
        else {
            Write-Info "Rule '$name' not found – skipping"
        }
    }
}

# ---------------------------------------------------------------------------
# 5. Optionally remove published files
# ---------------------------------------------------------------------------
if ($RemovePublish) {
    Write-Step "Removing published files ..."
    $resolved = $null
    try {
        $resolved = Resolve-Path -LiteralPath $PublishPath -ErrorAction Stop
    }
    catch {
        Write-Info "Publish path does not exist: $PublishPath – nothing to remove"
    }

    if ($resolved) {
        # Safety: ensure we are inside the repo before deleting
        $repoRoot = Resolve-Path -LiteralPath "$PSScriptRoot\.." -ErrorAction Stop
        if ($resolved.Path -and $resolved.Path.StartsWith($repoRoot.Path, [StringComparison]::OrdinalIgnoreCase)) {
            try {
                Remove-Item -LiteralPath $resolved.Path -Recurse -Force -ErrorAction Stop
                Write-Success "Published files removed from: $($resolved.Path)"
            }
            catch {
                Write-Warn "Could not remove publish directory: $_"
            }
        }
        else {
            Write-Warn "Publish path is outside the repository root – skipping deletion for safety: $($resolved.Path)"
        }
    }
}
else {
    Write-Info "Published files preserved (use -RemovePublish to delete them)"
}

# ---------------------------------------------------------------------------
# 6. Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║        ALT_ICS UNINSTALL COMPLETE             ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Service     : $ServiceName  [removed]"
Write-Host "  Firewall    : ALT_ICS rules  [removed]"
if ($RemovePublish) {
    Write-Host "  Published   : $PublishPath  [deleted]"
}
else {
    Write-Host "  Published   : preserved (use -RemovePublish to clean up)"
}
Write-Host ""
Write-Host "  To reinstall: .\scripts\install.ps1"
Write-Host ""
