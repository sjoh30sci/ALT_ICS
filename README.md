<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Passing" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Windows-10%2F11%20%7C%20Server%202019%2B-0078D6?logo=windows" alt="Windows" />
  <img src="https://img.shields.io/github/license/sjoh30sci/ALT_ICS" alt="License" />
</p>

<h1 align="center">ALT_ICS — Alternative Internet Connection Sharing</h1>

<p align="center">
  A custom, high-reliability replacement for native Windows Internet Connection Sharing (ICS).<br />
  Custom NAT engine · Built-in DHCP server · DNS relay with failover · Windows Service backend
</p>

---

## 📋 Table of Contents

- [Description](#-description)
- [Features](#-features)
- [Architecture](#-architecture)
- [Prerequisites](#-prerequisites)
- [Quick Start](#-quick-start)
- [Manual Installation](#-manual-installation)
- [Usage](#-usage)
- [Configuration](#-configuration)
- [Project Structure](#-project-structure)
- [Port Reference](#-port-reference)
- [Development](#-development)
- [Troubleshooting](#-troubleshooting)

---

## 🚀 Description

### The Problem

Windows native Internet Connection Sharing (ICS) is notoriously unreliable. It commonly breaks after **sleep/resume cycles**, **network profile changes**, **Windows updates**, or **adapter reconfigurations** — often requiring a full reboot or manual service restart. It offers no visibility into what is happening, no health monitoring, and no recovery mechanism.

### The Solution

**ALT_ICS** replaces the entire ICS stack with a purpose-built Windows Service that implements its own:

- **Network Address Translation (NAT)** engine — session-aware connection tracking with port allocation
- **DHCP server** — RFC 2131 compliant with configurable IP pools, lease management, and option delivery
- **DNS relay** — transparent proxy with primary/secondary failover and in-memory response caching

### Key Differentiators

| Area | Native Windows ICS | ALT_ICS |
|------|--------------------|---------|
| **NAT** | Closed, opaque API (WinNAT) | Fully custom, open, instrumented |
| **DHCP** | Hidden, no configuration | RFC 2131, configurable pool & lease |
| **DNS** | Not included | Built-in relay with failover |
| **Monitoring** | None (blind) | Real-time dashboard + Event Log |
| **Recovery** | Manual restart | Auto-reconnect, health monitoring, Windows service recovery |
| **Management** | netsh + GUI only | Spectre.Console CLI + SignalR |

---

## ✨ Features

- **Custom NAT Engine** — Session-based connection tracking with `ConcurrentDictionary`, automatic ephemeral port allocation (49152–65535), TCP/UDP support, session timeout cleanup, and throughput statistics.

- **DHCP Server (RFC 2131)** — Full DISCOVER / OFFER / REQUEST / ACK / NAK / DECLINE / RELEASE cycle, configurable IP pool ranges, lease duration, subnet mask, gateway, and DNS options delivered to clients.

- **DNS Relay with Failover** — Transparent raw-bytestream forwarding on UDP/53, primary/secondary upstream DNS with 5-second timeout fallback, in-memory response cache (30s TTL, max 1000 entries), and concurrent query handling.

- **SignalR Real-Time Communication** — Service exposes a SignalR hub at `/hubs/monitor` over TCP/51000; the GUI client subscribes to live NAT stats, health reports, and connection state changes with automatic exponential-backoff reconnection.

- **Windows Event Log Integration** — Structured logging to a dedicated `ALT_ICS` event log source with categorised event IDs (service lifecycle, NAT, DHCP, DNS, configuration, health, errors).

- **Health Monitoring & Auto-Recovery** — Periodic health checks (every 30s by default) across all three sub-services; Windows service recovery policy (restart on failure with escalating delays).

- **Spectre.Console CLI Dashboard** — Rich terminal UI with live-updating panels, coloured health indicators, and real-time throughput/session statistics.

---

## 🏗 Architecture

ALT_ICS is split into three .NET 8 projects following a clean separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                     ALT_ICS.Shared                          │
│  (Contracts, models, interfaces, logging helpers, constants) │
└──────────────────┬──────────────────────────────────────────┘
                   │ references
        ┌──────────┴──────────┐
        ▼                     ▼
┌─────────────────┐  ┌──────────────────┐
│ ALT_ICS.Service │  │   ALT_ICS.GUI    │
│ Windows Service  │  │ Spectre.Console  │
│ NAT · DHCP · DNS │  │ CLI + Dashboard  │
│ SignalR Hub      │◄─┤ SignalR Client   │
│ Event Logging    │  │                  │
└─────────────────┘  └──────────────────┘
```

| Project | Role |
|---------|------|
| **ALT_ICS.Shared** | Shared library — `NetworkConfig` model, `IConnectionManager`, `INATService`, `ISessionTable` interfaces, `CommonLogger` helpers, `Constants` |
| **ALT_ICS.Service** | Windows Service (Worker) — `NATConnectionService`, `DHCPServer`, `DNSRelayService`, `NetworkSharingService` orchestrator, `WindowsServiceHost` lifecycle, `ServiceEventLogger` |
| **ALT_ICS.GUI** | Console management app — `StartSharingCommand`, `StopSharingCommand`, `StatusCommand`, `ConfigCommand`, `DashboardCommand`, `ServiceClient` (SignalR) |

---

## 📦 Prerequisites

- **Windows 10/11** or **Windows Server 2019+**
- **.NET 8 Runtime** (or SDK for development)
- **Administrator privileges** (required for service installation, firewall rules, and IP forwarding)

---

## ⚡ Quick Start

The fastest way to build, install, and start ALT_ICS:

```powershell
# Clone the repository
git clone https://github.com/sjoh30sci/ALT_ICS.git
cd ALT_ICS

# Run the deployment orchestrator (elevates automatically)
.\scripts\deploy.ps1
```

The `deploy.ps1` script will:

1. ✅ Build the solution in Release mode
2. ✅ Install the `ALT_ICS` Windows Service (auto-start with recovery)
3. ✅ Configure Windows Firewall rules
4. ✅ Enable IP forwarding
5. ✅ Start the service
6. ✅ Run a quick health verification

---

## 🔧 Manual Installation

Step-by-step deployment without the automation script:

### 1. Build

```powershell
dotnet publish -c Release
```

### 2. Install the Windows Service

```powershell
# Create the service (auto-start)
sc.exe create ALT_ICS binPath= "C:\path\to\ALT_ICS.Service.exe" start= auto

# Set a description
sc.exe description ALT_ICS "Custom NAT-based internet connection sharing replacing Windows ICS"

# Configure recovery (restart on failure)
sc.exe failure ALT_ICS reset= 86400 actions= restart/60000/restart/120000/restart/300000
```

### 3. Configure Firewall

Allow DNS (UDP/53), DHCP (UDP/67), and SignalR (TCP/51000) through the firewall:

```powershell
.\scripts\setup-firewall.ps1
```

### 4. Start the Service

```powershell
sc.exe start ALT_ICS
```

### 5. Verify

```powershell
.\ALT_ICS.GUI.exe status
```

---

## 🎮 Usage

The `ALT_ICS.GUI.exe` console application provides a full management interface:

```powershell
# Show help
.\ALT_ICS.GUI.exe --help

# View service status (health of NAT, DHCP, DNS)
.\ALT_ICS.GUI.exe status

# Start internet sharing
.\ALT_ICS.GUI.exe start

# Stop internet sharing
.\ALT_ICS.GUI.exe stop

# View or modify configuration
.\ALT_ICS.GUI.exe config
.\ALT_ICS.GUI.exe config PublicInterface "Ethernet"

# Open the real-time monitoring dashboard
.\ALT_ICS.GUI.exe dashboard
.\ALT_ICS.GUI.exe dashboard --refresh 3   # custom refresh interval (seconds)
```

### Service Management (via Windows)

```powershell
# Query service status
sc.exe query ALT_ICS

# Stop the service
sc.exe stop ALT_ICS

# Start the service
sc.exe start ALT_ICS

# Uninstall the service
.\scripts\uninstall.ps1
```

---

## ⚙️ Configuration

Configuration is stored in `appsettings.json` under the `NetworkConfig` section, shared by both the Service and GUI projects.

```json
{
  "NetworkConfig": {
    "PublicInterface": "Wi-Fi",
    "PrivateInterface": "Ethernet",
    "PrivateSubnet": "192.168.137.0",
    "PrivateSubnetMask": "255.255.255.0",
    "GatewayIp": "192.168.137.1",
    "DhcpPoolStart": "192.168.137.100",
    "DhcpPoolEnd": "192.168.137.200",
    "DhcpLeaseTimeMinutes": 1440,
    "PrimaryDns": "8.8.8.8",
    "SecondaryDns": "8.8.4.4",
    "AutoStart": true,
    "HealthCheckIntervalSeconds": 30,
    "VerboseLogging": false
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `PublicInterface` | `Wi-Fi` | Upstream (internet-facing) network adapter |
| `PrivateInterface` | `Ethernet` | Downstream (client-facing) network adapter |
| `PrivateSubnet` | `192.168.137.0` | Private network subnet |
| `PrivateSubnetMask` | `255.255.255.0` | Subnet mask for the private network |
| `GatewayIp` | `192.168.137.1` | Gateway IP assigned to the private interface |
| `DhcpPoolStart` | `192.168.137.100` | First IP in the DHCP lease pool |
| `DhcpPoolEnd` | `192.168.137.200` | Last IP in the DHCP lease pool |
| `DhcpLeaseTimeMinutes` | `1440` | DHCP lease duration (24 hours) |
| `PrimaryDns` | `8.8.8.8` | Primary upstream DNS server |
| `SecondaryDns` | `8.8.4.4` | Secondary upstream DNS server (fallback) |
| `AutoStart` | `true` | Automatically start sharing when the service boots |
| `HealthCheckIntervalSeconds` | `30` | Interval between health checks |
| `VerboseLogging` | `false` | Enable verbose diagnostic logging |

---

## 📁 Project Structure

```
ALT_ICS/
├── ALT_ICS.Shared/                      # Shared library
│   ├── Models/
│   │   ├── IInternetConnectionSharing.cs  # IConnectionManager, HealthReport, ServiceState
│   │   ├── Interfaces/
│   │   │   └── IProgram.cs                # INATService, ITranslationEntry, ISessionTable, NATStats
│   │   └── NetworkConfig.cs               # Configuration model
│   ├── Utils/
│   │   ├── CommonLogger.cs                # Shared logging extensions
│   │   └── Constants.cs                   # App-wide constants
│   └── ALT_ICS.Shared.csproj
│
├── ALT_ICS.Service/                     # Windows Service
│   ├── Services/
│   │   ├── NATConnectionService.cs        # Custom NAT engine (session tracking, port allocation)
│   │   ├── DHCPServer.cs                  # RFC 2131 DHCP server
│   │   ├── DNSRelayService.cs             # Transparent DNS relay with failover
│   │   ├── NetworkSharingService.cs       # Orchestrator (coordinates NAT/DHCP/DNS)
│   │   └── NativeMethods.cs               # Windows P/Invoke declarations
│   ├── Logging/
│   │   └── ServiceEventLogger.cs          # Windows Event Log wrapper
│   ├── WindowsServiceHost.cs              # BackgroundService lifecycle host
│   ├── Program.cs                         # DI setup and entry point
│   ├── appsettings.json                   # Configuration (NetworkConfig + Logging)
│   └── ALT_ICS.Service.csproj
│
├── ALT_ICS.GUI/                         # Console Management UI
│   ├── Commands/
│   │   └── ServiceCommands.cs             # DefaultCommand, Start/Stop/Status/Config/Dashboard
│   ├── Services/
│   │   └── ServiceClient.cs               # SignalR client with auto-reconnect
│   ├── Views/
│   │   └── DashboardView.cs               # Spectre.Console live dashboard
│   ├── Utils/
│   │   └── ConsoleUtils.cs                # Banner, spinner, table helpers
│   ├── TypeRegistrar.cs                   # Spectre.Console DI integration
│   ├── Program.cs                         # CLI entry point
│   ├── appsettings.json
│   └── ALT_ICS.GUI.csproj
│
├── scripts/                             # Deployment & management scripts
│   ├── build.ps1                          # Build and publish the solution
│   ├── install.ps1                        # Install/update the Windows Service
│   ├── uninstall.ps1                      # Remove the Windows Service
│   ├── setup-firewall.ps1                 # Configure Windows Firewall rules
│   ├── deploy.ps1                         # Full deployment orchestrator
│   ├── elevate_deploy.bat                 # UAC elevation helper
│   ├── elevate.vbs                        # VBScript elevation helper
│   ├── launch_elevated.ps1                # Self-elevating PowerShell
│   ├── run-deploy-as-admin.bat            # One-click admin deploy launcher
│   └── README.deploy.md                   # Deployment guide
│
├── ALT_ICS.sln                          # Solution file (Visual Studio 2022)
├── .editorconfig                        # Code style rules
├── .gitignore                           # Git ignore rules
└── README.md                            # This file
```

---

## 🔌 Port Reference

| Port | Protocol | Service | Description |
|------|----------|---------|-------------|
| 53 | UDP | DNS Relay | Forwards DNS queries from private clients to upstream servers |
| 67 | UDP | DHCP Server | Listens for DHCP DISCOVER/REQUEST from private clients |
| 51000 | TCP | SignalR Hub | Real-time communication channel between GUI and Service |
| 51001 | TCP | Health HTTP | Exposes service health status (`GET /health`) |

---

## 🛠 Development

### Building from Source

```powershell
# Prerequisites: .NET 8 SDK installed

# Clone the repo
git clone https://github.com/sjoh30sci/ALT_ICS.git
cd ALT_ICS

# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run tests (when available)
dotnet test

# Publish for deployment
dotnet publish -c Release
```

### Debugging

1. Open `ALT_ICS.sln` in Visual Studio 2022 (or JetBrains Rider)
2. Set `ALT_ICS.Service` as the startup project
3. Run as Administrator (required for raw sockets and IP forwarding)
4. Use `ALT_ICS.GUI` as a separate startup project for the management interface

Alternatively, run the service in console mode for live logging:

```powershell
dotnet run --project ALT_ICS.Service
```

### Project Conventions

- **Target**: .NET 8.0, Windows-only
- **Language features**: Nullable enabled, ImplicitUsings enabled
- **Logging**: Structured logging with categorised `EventId` values via `Microsoft.Extensions.Logging`
- **Configuration**: `appsettings.json` bound to strongly-typed `NetworkConfig` model
- **Communication**: SignalR hub pattern (Service as server, GUI as client)
- **P/Invoke**: All native methods in `NativeMethods.cs` with `SafeSocketHandle`

---

## 🔍 Troubleshooting

### Service fails to start

| Symptom | Likely Cause | Solution |
|---------|-------------|----------|
| `sc.exe start ALT_ICS` returns immediately with failure | Missing dependencies | Check the Application Event Log for details |
| Service starts but stops after a few seconds | Port conflicts (53 or 67 already in use) | Run `netstat -an` to check if another DNS/DHCP service is running |
| `Access Denied` errors | Not running as Administrator | Ensure the service runs as `LocalSystem` and scripts are run elevated |

### Clients can't get IP addresses

- Verify the DHCP server is running: `.\ALT_ICS.GUI.exe status`
- Check that UDP port 67 is not blocked by firewall
- Confirm `PrivateInterface` matches the downstream adapter name
- Ensure the private interface has the gateway IP (`192.168.137.1`) assigned

### DNS not resolving

- Verify the DNS relay is running: `.\ALT_ICS.GUI.exe status`
- Test upstream DNS directly: `nslookup google.com 8.8.8.8`
- Check if port 53 is available: `netstat -an | findstr ":53 "`
- Ensure no other DNS service (e.g., Windows DNS Server, pi-hole) is bound to port 53

### No internet access via shared connection

1. Check service health: `.\ALT_ICS.GUI.exe status`
2. Verify IP forwarding is enabled: `Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" -Name IPEnableRouter`
3. Confirm the client has received a valid IP, subnet mask, gateway, and DNS via DHCP
4. Check the Windows Event Log under `ALT_ICS` source for errors

### Viewing Logs

```powershell
# Windows Event Log
# Open Event Viewer → Windows Logs → Application → Source: ALT_ICS
Get-WinEvent -LogName "ALT_ICS" -MaxEvents 50 | Format-Table TimeCreated, LevelDisplayName, Message -AutoSize
```

---

## 📄 License

This project is licensed under the terms specified in the repository. See the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Built with ❤️ for a more reliable internet sharing experience on Windows.<br />
  <sub>ALT_ICS — Because Windows ICS should just work.</sub>
</p>
