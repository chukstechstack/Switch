
<div align="center">

  <img src="https://img.shields.io/badge/SWITCH-DESKTOP_OS-emerald?style=for-the-badge&logo=windows&logoColor=white" alt="Switch Logo" />
  
  # ⚡ SWITCH_
  
  <p align="center">
    <b>The Ultimate Minimalist Focus & Goal Execution Engine for Windows.</b><br>
    <i>Lock distractions out, channel total flow state, and enforce deep focus.</i>
  </p>

  <p align="center">
    <a href="https://switch-landing.onrender.com/" target="_blank">
      <img src="https://img.shields.io/badge/🚀_Live_Landing_Page-Preview_Web-10B981?style=flat-square&logo=safari" alt="Live Landing Page" />
    </a>
    <a href="https://github.com/chukstechstack/SwitchInstaller/releases/latest/download/SwitchInstaller.msi" target="_blank">
      <img src="https://img.shields.io/badge/📦_Download_PC_Installer-.MSI_Release-3B82F6?style=flat-square&logo=windows" alt="Download Installer" />
    </a>
    <img src="https://img.shields.io/badge/Platform-Windows_10_%2F_11-blue?style=flat-square&logo=windows" alt="Platform" />
    <img src="https://img.shields.io/badge/.NET-10.0-purple?style=flat-square&logo=dotnet" alt=".NET Version" />
  </p>

</div>

---

## 🎬  Architecture & Overview

> **Switch** is a high-assurance Windows desktop application engineered to give you absolute control over your digital environment. By combining a modern WPF interface with a persistent background Windows service, Switch keeps enforcement active even when the app window is closed.

<div align="center">
  <br>
  <img src="https://github.com/user-attachments/assets/50d1129b-a669-4b99-9e9f-e6d25d4ae742" alt="Switch App Interface" width="90%" style="border-radius: 16px; border: 1px solid rgba(255,255,255,0.15); box-shadow: 0 20px 50px rgba(0,0,0,0.8);" />
  <br><br>
</div>

---

## 🌐 Live Experience & Installation

*   🌟 **Test in Browser:** Check out the live landing page at **[switch-landing.onrender.com](https://switch-landing.onrender.com/)**.
*   💻 **Get the Desktop App:** 
    1. Head over to the **[Releases Page](https://github.com/chukstechstack/SwitchInstaller/releases/latest)**.
    2. Download the latest `SwitchInstaller.msi`.
    3. Run the installer and launch **SWITCH** from your desktop.

## ✨ Why Switch?

Switch is built for developers, creators, and high achievers who want a clean, intentional control layer over their OS:

*   🛑 **App & Process Blocking** — Target distracting desktop applications, browser processes, and installer patterns.
*   📁 **Folder Locking** — Apply strict access-control protections to directories during active focus windows.
*   🛡️ **Service-Backed Enforcement** — Runs a dedicated Windows service (`Switch.Service.Host`) to keep monitoring active independently of the UI.
*   🔒 **Uninstall Safety** — Actively guards against tampering and bypass flows while focus blocks are enforced.
*   🔄 **Auto-Start Integration** — Registers seamlessly into system startup so rules apply the moment your PC boots.

---

## 🛠️ Technology Stack

*   🖥️ **WPF (Windows Presentation Foundation)** — Modern, responsive desktop user interface.
*   ⚙️ **C# / .NET 10** — High-performance backend logic and service communication.
*   📦 **WiX Toolset** — Professional `.msi` installation package generation.
*   🌐 **React & Tailwind CSS** — Powers the companion cinematic web landing page.

---

## 🚀 Quick Start & Development

### 1. Clone the Repository
```bash
git clone [https://github.com/chukstechstack/Switch.git](https://github.com/chukstechstack/Switch.git)
cd Switch

```

### 2. Restore Dependencies

```powershell
dotnet restore

```

### 3. Build the Application

```powershell
dotnet build "Switch.csproj" -c Release

```

### 4. Publish for Production

```powershell
dotnet publish "Switch.csproj" -c Release -r win-x64 --self-contained false -o "publish\final"

```

Launch the executable directly:

```powershell
"publish\final\Switch.exe"

```

---

## ⚙️ Background Service Setup

To enable full background watchdog and system enforcement:

```powershell
cd publish\final
.\Switch.Service.Host.exe --install

```

*This command registers the background Windows service and starts protection protocols automatically.*

---

## 🔒 Security Model & Realities

Switch is designed as a powerful productivity enforcement utility for personal and managed Windows environments:

* The app utilizes a real native Windows service as its core system boundary for monitoring and rule persistence.
* It is optimized for focus and intentional workflow guardrails rather than enterprise kernel-level root-hardening.

---

## 🗺️ Roadmap

* [x] WPF Desktop UI & Core Block State Engine
* [x] Background Windows Service Watchdog (`Switch.Service.Host`)
* [x] Cinematic Web Landing Page & Automated MSI Release Pipeline
* [ ] Advanced Recurrence Scheduling & Analytics Trackers
* [ ] Kiosk-Style Enterprise Hardening Extensions

---

## 📄 License & Author

Distributed under internal/personal developer terms.

**Created & Maintained by C.E Kingsley** 🚀
