# ALT_ICS — Deployment Guide

> **ALT_ICS (Alternative Internet Connection Sharing)** is a custom NAT-based service
> that replaces the built-in Windows Internet Connection Sharing (ICS) feature.
> It runs as a Windows Service and provides DNS relay, DHCP server, and a
> SignalR-based control channel.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| **Windows 10 / Windows Server 2019+** | The service uses Windows-native APIs (`Win32_NAT`, `netsh`, etc.) |
| **.NET 8 Runtime** | Required unless publishing as self-contained. Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Administrator privileges** | All scripts check / require elevation for service and firewall operations |
| **Two network interfaces** | One public (Internet-facing) and one private (shared network) — configured in `appsettings.json` |

> Note: `PublishSingleFile` + `PublishReadyToRun` is enabled in the `.csproj`,
> but `SelfContained` is **false** by default — the .NET 8 runtime must be
> present on the target machine.

---

## Quick Start

From a PowerShell **Administrator** prompt:

```powershell
powershell -ExecutionPolicy Bypass .\scripts\deploy.ps1
```

This single command will:

1. Restore NuGet packages and build the solution
2. Publish `ALT_ICS.Service` as a single-file, ReadyToRun executable
3. Install the Windows Service (`ALT_ICS`) with auto-start and recovery
4. Create Windows Firewall inbound rules for the required ports
5. Enable IP forwarding (`IPEnableRouter = 1`)
6. Start the service and verify it is running

---

## Manual Installation (Step by Step)

### 1. Build

```powershell
.\scripts\build.ps1
```

Output is placed in `ALT_ICS.Service\bin\Release\net8.0\publish\`.

### 2. Install the Service

```powershell
.\scripts\install.ps1
```

This script:

- Checks for admin rights and .NET 8 runtime
- Uses the built binaries from the default publish path
- Creates the service with `sc.exe create`
- Configures auto-start and recovery (restart after 60s / 120s / 300s)
- Creates firewall rules
- Enables IP forwarding
- Starts the service

### 3. Configure Firewall (standalone)

```powershell
.\scripts\setup-firewall.ps1
```

Creates inbound rules for:

| Rule Name | Port | Protocol |
|---|---|---|
| `ALT_ICS - DNS` | 53 | UDP |
| `ALT_ICS - DHCP` | 67 | UDP |
| `ALT_ICS - SignalR` | 51000 | TCP |
| `ALT_ICS - Health` | 51001 | TCP |

### 4. Verify the Service

**Using Service Control Manager:**

```powershell
sc.exe query ALT_ICS
```

**Using the health endpoint** (PowerShell):

```powershell
Invoke-RestMethod -Uri http://localhost:51001/health
```

**Using services.msc:**

- Press `Win + R`, type `services.msc`
- Look for **"ALT_ICS — Alternative Internet Connection Sharing"**

**Check Event Logs:**

- Open Event Viewer → Windows Logs → Application
- Filter by source `ALT_ICS`

---

## Configuration

The service reads settings from `ALT_ICS.Service\appsettings.json`. Key settings:

| Setting | Default | Description |
|---|---|---|
| `PublicInterface` | `Wi-Fi` | The network adapter connected to the Internet |
| `PrivateInterface` | `Ethernet` | The network adapter for the private LAN |
| `PrivateSubnet` | `192.168.137.0/24` | Subnet for the private network |
| `GatewayIp` | `192.168.137.1` | Gateway IP assigned to the private adapter |
| `DhcpPoolStart` / `DhcpPoolEnd` | `.100–.200` | DHCP lease range |
| `PrimaryDns` / `SecondaryDns` | `8.8.8.8` / `8.8.4.4` | Upstream DNS servers |
| `AutoStart` | `true` | Start NAT and DHCP automatically |
| `HealthCheckIntervalSeconds` | `30` | Health endpoint refresh interval |

Edit the file before running `install.ps1` or `deploy.ps1` for custom settings.

---

## Advanced Options

### Custom publish path

```powershell
.\scripts\install.ps1 -PublishPath "C:\MyBuild\publish" -SkipBuild
```

### Build without R2R / single-file

Edit the `.csproj` and set `PublishSingleFile` and `PublishReadyToRun` to `false`, then:

```powershell
.\scripts\build.ps1
```

### Skip firewall or IP forwarding during install

```powershell
.\scripts\install.ps1 -SkipFirewall -SkipIpForward
```

---

## Uninstallation

### Quick uninstall

```powershell
powershell -ExecutionPolicy Bypass .\scripts\uninstall.ps1
```

### Full uninstall (including binaries)

```powershell
.\scripts\uninstall.ps1 -RemovePublish -Force
```

The uninstaller will:

1. Stop and delete the Windows Service
2. Remove all ALT_ICS firewall rules
3. Optionally delete the published binaries
4. **Note:** IP forwarding (`IPEnableRouter`) is **not** reverted — set it back manually if needed:

```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name IPEnableRouter -Value 0
```

---

## Port Reference

| Port | Protocol | Direction | Service | Purpose |
|---|---|---|---|---|
| 53 | UDP | Inbound | DNS Relay | Resolves DNS queries from private LAN clients |
| 67 | UDP | Inbound | DHCP Server | Assigns IP addresses to private LAN clients |
| 51000 | TCP | Inbound | SignalR | Control channel for the GUI / remote management |
| 51001 | TCP | Inbound | Health | HTTP health-check endpoint (returns 200 OK) |

---

## Troubleshooting

### Service fails to start

1. Check the **Event Log** (source: `ALT_ICS`) for error messages
2. Verify the binary path in `sc.exe qc ALT_ICS` points to the correct `.exe`
3. Confirm .NET 8 runtime is installed: `dotnet --list-runtimes`
4. Ensure the configured network interfaces exist: `Get-NetAdapter`

### Firewall rules not working

```powershell
Get-NetFirewallRule -DisplayName "ALT_ICS -*" | Format-Table Name, Enabled, Direction, Action
```

### "Access denied" errors

All scripts must run as **Administrator**. Restart PowerShell with "Run as administrator".

### IP forwarding not taking effect

A **reboot** may be required after setting `IPEnableRouter = 1`. Alternatively:

```powershell
# Enable without reboot (requires admin):
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name IPEnableRouter -Value 1
```

Then start the `RemoteAccess` service:

```powershell
Set-Service -Name RemoteAccess -StartupType Manual
Start-Service -Name RemoteAccess
```

---

## Script Reference

| Script | Description |
|---|---|
| `build.ps1` | Restore, build (Release), publish to `bin\Release\net8.0\publish` |
| `install.ps1` | Install Windows Service, firewall, IP forwarding |
| `uninstall.ps1` | Remove service, firewall rules, optionally delete binaries |
| `setup-firewall.ps1` | Create (or remove) inbound firewall rules |
| `deploy.ps1` | **Full deployment**: build + install + firewall + verify |
