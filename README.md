# Switch

Switch is a Windows desktop app designed to help users take control of digital focus and productiveness by blocking distracting apps, browser installers, or folders during a scheduled session. It combines a polished desktop UI with a background Windows service that keeps enforcement active even when the app window is closed.

## Why Switch

The app is built for people who want a simple, intentional control layer over their digital environment:

- block websites and desktop apps during focus windows
- lock folders or directories during active sessions
- enforce session rules even while the main UI is closed
- protect the active blocking state from casual bypass
- support scheduled or time-based focus blocks

## What it does

Switch gives you a modern blocker experience with:

- a WPF desktop interface
- app and folder blocking
- browser and installer blocking logic
- service-backed enforcement
- automatic startup support
- persistent block state across sessions
- uninstall protection while active blocks exist

## Core features

### App blocking

Block specific applications by name, including browser processes and common installer patterns.

### Folder locking

Apply access-control protections to folders during active lock periods so blocked content cannot be opened or modified.

### Focus sessions

Define a block period, then let Switch keep enforcing it in the background while you stay focused.

### Service-backed protection

The app installs and runs a Windows service to keep enforcement active independently of the main UI.

### Auto-start support

Switch can register itself to run on Windows startup so the app is available immediately when the computer boots.

### Uninstall safety

When active blocks exist, uninstall flows are blocked to prevent easy bypass or tampering.

## Architecture

Switch is structured as a desktop application plus a background Windows service:

- Switch UI: WPF application for user interaction
- Switch.Service.Host: Windows service for enforcement and watchdog behavior
- Block persistence: JSON-backed block store for active rules
- Enforcement: process monitoring plus folder locking

## Project layout

- `Switch.csproj` — main desktop application
- `Service/` — background service host and enforcement logic
- `Model/` — data models used by the app
- `Pages/` — UI pages and flows
- `Installer/` — WiX installer files
- `UninstallChecker/` — uninstall guard helper
- `Tests/` — project tests and validation

## Requirements

- Windows 10 or Windows 11
- .NET 10
- Windows desktop development workload
- Administrative rights for service installation and OS integration steps

## Getting started

### 1. Clone the repo

```bash
git clone <your-repository-url>
cd Switch
```

### 2. Restore dependencies

```powershell
dotnet restore
```

### 3. Build the app

```powershell
dotnet build "Switch.csproj" -c Release
```

### 4. Publish the app

```powershell
dotnet publish "Switch.csproj" -c Release -r win-x64 --self-contained false -o "publish\final"
```

Then launch:

```powershell
"C:\Users\HP\source\repos\Switch\publish\final\Switch.exe"
```

## Service setup

If you want the service-backed watchdog and enforcement behavior enabled:

```powershell
cd "C:\Users\HP\source\repos\Switch\publish\final"
.\Switch.Service.Host.exe --install
```

This registers the service and starts it automatically.

## Notes on security model

Switch is designed as a practical Windows desktop enforcement tool, not a kernel-level anti-tamper system.

Important realities:

- a normal desktop app cannot fully guarantee it is impossible to terminate by an administrator on a machine they control
- the app uses a real Windows service to keep enforcement active in the background
- the service is the primary system boundary for protection logic
- OS-level lockdown is only possible through enterprise policy, kiosk configuration, or kernel-level drivers

This means Switch is best understood as a high-assurance desktop enforcement tool for managed or personal environments, not a generic root-level system hardening product.

## License

This project is provided for educational, internal, or personal use unless another license is specified in the repository.

## Roadmap

Planned growth areas include:

- more precise scheduling and recurring focus windows
- improved installer detection and browser blocking
- stronger service watchdog behavior
- polished reporting and analytics for active blocks
- deployment via installer and managed packaging
- kiosk-style hardening for enterprise use

## Contributing

Contributions are welcome. If you want to help improve the project:

1. create a feature branch
2. make a focused change
3. validate it locally
4. open a clean pull request

## Support

This project is meant to be a practical desktop utility and not a replacement for enterprise endpoint security tooling.

## Status

Switch is currently in active development as a desktop blocker/enforcement app with service-backed monitoring and protection logic.

---

Built for focus. Hardened for persistence. Designed for personal and managed Windows environments.
