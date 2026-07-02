<#
.SYNOPSIS
    Creates Windows Firewall inbound rules required by ALT_ICS.
.DESCRIPTION
    Adds inbound rules for DNS (UDP 53), DHCP (UDP 67), SignalR (TCP 51000),
    and Health (TCP 51001).  Existing rules with the same names are removed
    first so they can be recreated with the latest configuration.
.PARAMETER RemoveOnly
    If set, only removes the firewall rules without creating them.
.EXAMPLE
    .\scripts\setup-firewall.ps1
.EXAMPLE
    .\scripts\setup-firewall.ps1 -RemoveOnly
#>

[CmdletBinding()]
param(
    [switch]$RemoveOnly
)

$ErrorActionPrevention = "Stop"

# Define the rules
$rules = @(
    @{ Name = "ALT_ICS - DNS";    Port = 53;  Protocol = "UDP"; Description = "ALT_ICS DNS relay (inbound)" }
    @{ Name = "ALT_ICS - DHCP";   Port = 67;  Protocol = "UDP"; Description = "ALT_ICS DHCP server (inbound)" }
    @{ Name = "ALT_ICS - SignalR"; Port = 51000; Protocol = "TCP"; Description = "ALT_ICS SignalR control channel" }
    @{ Name = "ALT_ICS - Health";  Port = 51001; Protocol = "TCP"; Description = "ALT_ICS health-check endpoint" }
)

function Write-Status {
    param([string]$Message, [string]$Color = "Cyan")
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host "FIREWALL" -NoNewline -ForegroundColor $Color
    Write-Host "] $Message" -ForegroundColor Gray
}

function Write-Success {
    param([string]$Message)
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host "    OK" -NoNewline -ForegroundColor Green
    Write-Host "] $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[" -NoNewline -ForegroundColor Gray
    Write-Host " WARN" -NoNewline -ForegroundColor Yellow
    Write-Host "] $Message" -ForegroundColor Gray
}

# ---- Admin check ----
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    exit 1
}

# ---- Remove existing rules ----
foreach ($rule in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Status "Removing existing rule '$($rule.Name)'..."
        $existing | Remove-NetFirewallRule -ErrorAction Stop
        Write-Success "Removed rule '$($rule.Name)'"
    }
}

if ($RemoveOnly) {
    Write-Host ""
    Write-Host "Firewall rules removed.  Exiting (-RemoveOnly was specified)." -ForegroundColor Yellow
    exit 0
}

# ---- Create new rules ----
foreach ($rule in $rules) {
    Write-Status "Creating rule '$($rule.Name)' ($($rule.Protocol):$($rule.Port))..."
    try {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Description $rule.Description `
            -Direction Inbound `
            -LocalPort $rule.Port `
            -Protocol $rule.Protocol `
            -Action Allow `
            -Profile Any `
            -Enabled True `
            -ErrorAction Stop | Out-Null
        Write-Success "Created rule '$($rule.Name)'"
    }
    catch {
        Write-Warn "Failed to create rule '$($rule.Name)': $_"
    }
}

# ---- Summary ----
Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
if ($RemoveOnly) {
    Write-Host " FIREWALL RULES REMOVED" -ForegroundColor Yellow
}
else {
    Write-Host " FIREWALL RULES CONFIGURED" -ForegroundColor Green
}
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
foreach ($rule in $rules) {
    $status = if ($RemoveOnly) { "removed" } else { "active" }
    Write-Host "  $($rule.Name) : $($rule.Protocol)/$($rule.Port)  [$status]" -ForegroundColor Gray
}
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
