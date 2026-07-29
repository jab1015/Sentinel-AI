# Sentinel AI

> **AI-Powered Windows Security & System Intelligence**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It combines native Windows monitoring, security intelligence, and future AI-assisted recommendations in a clear, user-focused dashboard.

Sentinel AI is designed to explain what is happening on a Windows system, why it matters, and what actions may improve security, reliability, and performance.

---

# Project Status

**Version:** 0.3.0  
**Status:** Active Development  
**Completed Sprint:** Sprint 3 — Native Windows Monitoring  
**Current Sprint:** Sprint 4 — Monitoring Expansion

Sprint 3 established and verified the production CPU and physical-memory monitoring foundation.

Current Sprint 4 work focuses on displaying and verifying disk data, implementing live network throughput, expanding process monitoring, and adding production Defender and Firewall status checks.

---

# Verified Production Features

- WinUI 3 desktop application
- .NET 8 runtime
- MonitoringEngine and SystemSnapshot architecture
- Microsoft.Windows.CsWin32 integration
- Native CPU monitoring through `GetSystemTimes`
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- Real CPU utilization displayed on the dashboard
- Real used, total, and percentage physical-memory values displayed on the dashboard
- One-second dashboard refresh
- Live timestamp updates
- Successful build and application launch
- Runtime verification by the Product Owner

---

# Implemented Service Capabilities Awaiting Dashboard Completion

The repository already contains service-level support for:

- System-drive total capacity
- System-drive free space
- System-drive used space
- System-drive usage percentage
- Running process count

These values are collected by MonitoringEngine, but the current MainWindow displays only CPU, memory, and timestamp.

---

# Current Limitations

- Disk values are not yet rendered by the dashboard
- Network download throughput remains zero
- Network upload throughput remains zero
- Process count is not yet rendered by the dashboard
- Process intelligence is incomplete
- Microsoft Defender operational status is incomplete
- Windows Firewall operational status is incomplete
- Monitoring integration tests are not yet complete

---

# Verified Native Monitoring

## CPU Monitoring

CPU utilization is calculated from consecutive Windows system-time samples obtained through:

- `PInvoke.GetSystemTimes`
- Idle time
- Kernel time
- User time

The first sample returns zero because a previous sample is required. Invalid or reversed samples are handled safely, and calculated values are constrained to 0–100 percent.

## Physical Memory Monitoring

Physical-memory statistics are obtained through:

- `PInvoke.GlobalMemoryStatusEx`
- Total physical memory
- Available physical memory
- Used physical memory
- Percentage used

Win32 failures are handled without terminating the dashboard.

---

# Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 8 |
| UI Framework | WinUI 3 |
| Native API Generation | Microsoft.Windows.CsWin32 |
| Native APIs | GetSystemTimes, GlobalMemoryStatusEx |
| Target Platform | Windows 10 and later |
| Version Control | Git |
| Repository | GitHub |
| Production Branch | main |
| Build System | MSBuild |

---

# Repository Structure

```text
Sentinel-AI/
├── docs/
├── assets/
├── installer/
├── src/
│   └── SentinelAI/
│       └── Sentinel.App/
│           └── Sentinel.App/
│               ├── Models/
│               ├── Services/
│               ├── NativeMethods.txt
│               ├── MainWindow.xaml
│               └── MainWindow.xaml.cs
├── tests/
├── CHANGELOG.md
├── PRODUCT_REQUIREMENTS.md
└── README.md
```

---

# Architecture

Sentinel AI uses a layered, snapshot-based monitoring architecture:

```text
User Interface
      │
Monitoring Engine
      │
System Snapshot
      │
Monitor Services
      │
Windows APIs
```

Core principles:

- Single responsibility
- Loose coupling
- High cohesion
- Snapshot-based communication
- Native Windows integration
- Graceful failure handling
- Maintainable service boundaries
- Explainable future AI behavior

---

# Sprint 3 — Complete

- [x] Add Microsoft.Windows.CsWin32
- [x] Add `NativeMethods.txt`
- [x] Generate `GetSystemTimes`
- [x] Generate `GlobalMemoryStatusEx`
- [x] Replace random CPU values
- [x] Implement consecutive CPU sampling
- [x] Handle first and invalid CPU samples
- [x] Clamp CPU utilization to 0–100 percent
- [x] Report physical memory used
- [x] Report total physical memory
- [x] Report physical-memory percentage
- [x] Connect CPU and memory to MonitoringEngine
- [x] Display CPU and memory on the dashboard
- [x] Verify successful build
- [x] Verify successful application launch
- [x] Verify runtime behavior

---

# Sprint 4 — Active

Priority order:

1. Display existing disk metrics
2. Verify disk runtime values
3. Implement network download and upload throughput
4. Display process count and expand process intelligence
5. Implement Microsoft Defender operational status
6. Implement Windows Firewall operational status
7. Add monitoring integration and failure-path tests

---

# Roadmap

## Version 0.3

- Native CPU monitoring
- Native physical-memory monitoring
- CsWin32 integration
- Live dashboard updates
- Production system-monitor foundation

## Version 0.4

- Disk dashboard integration
- Network throughput
- Process intelligence
- Microsoft Defender integration
- Windows Firewall integration
- Monitoring tests

## Version 0.5

- Event Log intelligence
- Threat analysis engine
- Risk and confidence scoring
- AI recommendations
- Explainable AI

## Version 0.6

- Notification center
- Historical reporting
- Performance analytics
- Security timeline

## Version 1.0

- Commercial release
- MSIX installer
- Automatic updates
- Complete documentation
- Enterprise-ready architecture

---

# Development Workflow

Every feature follows the same workflow:

1. Review project status and implementation tracker
2. Review repository call sites and architecture
3. Implement
4. Build
5. Run
6. Verify
7. Update documentation
8. Commit
9. Push to `main`

The application must remain buildable and runnable throughout development.

---

# Build Requirements

- Windows 10 or Windows 11
- Visual Studio with the WinUI application-development workload
- .NET 8 SDK
- Windows App SDK dependencies restored through NuGet

---

# Documentation

Primary tracking documents:

- `docs/SAI-000_Project_Status.md`
- `docs/SAI-004_Sprint_History.md`
- `docs/SAI-005_Product_Roadmap.md`
- `docs/SAI-008_Release_Checklist.md`
- `docs/SAI-031_Implementation_Tracker.md`
- `CHANGELOG.md`

---

# Long-Term Vision

Sentinel AI is intended to become a professional Windows security platform capable of:

- Monitoring system health
- Detecting suspicious behavior
- Explaining security risks
- Providing AI-powered recommendations
- Assisting users with system maintenance
- Supporting enterprise environments

---

# License

This project is currently under active development. Licensing terms will be finalized before the first public release.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.