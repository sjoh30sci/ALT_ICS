# Building the ALT_ICS Installer

## Prerequisites
1. [Inno Setup](https://jrsoftware.org/isdl.php) (v6+ recommended)
2. .NET 8 SDK

## Build Steps
```powershell
# From the installer directory
.\BUILD_INSTALLER.ps1
```

The compiled installer will be at `installer\ALT_ICS_Setup_v1.0.0.exe`.

## What the Installer Does
- Copies ALT_ICS to `Program Files\ALT_ICS`
- Installs the Windows Service (auto-start)
- Adds firewall rules for DNS, DHCP, SignalR, and Health ports
- Enables IP forwarding
- Creates Start Menu shortcuts
- Provides full uninstall support
