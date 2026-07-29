# Sentinel AI

> **AI-Powered Windows Security & System Intelligence**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It combines native Windows monitoring, security intelligence, and future AI-assisted recommendations in a clear, user-focused dashboard.

Sentinel AI is designed to explain what is happening on a Windows system, why it matters, and what actions may improve security, reliability, and performance.

---

# Project Status

**Version:** 0.3.0  
**Status:** Active Development  
**Current Sprint:** Sprint 3 — Native Windows Monitoring

Sprint 3 has established the production system-monitoring foundation:

- Native Windows API integration through Microsoft.Windows.CsWin32
- Production CPU monitoring using `GetSystemTimes`
- Production physical-memory monitoring using `GlobalMemoryStatusEx`
- Live one-second dashboard refresh
- Snapshot-based monitoring architecture
- Verified successful build and runtime operation

---

# Current Features

The current implementation includes:

- WinUI 3 desktop application
- Modern Windows dashboard
- Monitoring engine and system snapshot model
- Real-time CPU usage monitoring
- Real-time physical-memory used, total, and percentage reporting
- CsWin32-generated native API bindings
- Live dashboard timestamps
- Disk monitoring framework
- Network monitoring framework
- Process monitoring framework
- Windows information monitoring
- Security monitoring framework
- Modular service architecture
- Engineering and release documentation

---

# Verified Native Monitoring

## CPU Monitoring

CPU utilization is calculated from consecutive Windows system-time samples obtained through:

- `PInvoke.GetSystemTimes`
- Idle time
- Kernel time
- User time

The first sample returns zero because a previous sample is required to calculate utilization. Later values are constrained to the valid range of 0–100 percent.

## Physical Memory Monitoring

Physical-memory statistics are obtained through:

- `PInvoke.GlobalMemoryStatusEx`
- Total physical memory
- Available physical memory
- Used physical memory
- Percentage used

Win32 failures are handled gracefully without crashing the dashboard.

---

# Technology Stack

| Component | Technology |
|-----------|------------|
| Language | C# |
| Framework | .NET 8 |
| UI Framework | WinUI 3 |
| Native API Generation | Microsoft.Windows.CsWin32 |
| Windows APIs | GetSystemTimes, GlobalMemoryStatusEx |
| IDE | Visual Studio |
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

# Current Sprint

Sprint 3 replaces placeholder system metrics with production-quality Windows monitoring.

## Completed

- [x] Add Microsoft.Windows.CsWin32
- [x] Add `NativeMethods.txt`
- [x] Generate `GetSystemTimes`
- [x] Generate `GlobalMemoryStatusEx`
- [x] Replace random CPU values
- [x] Implement consecutive CPU sampling
- [x] Return zero on the first CPU sample
- [x] Clamp CPU utilization to 0–100 percent
- [x] Report physical memory used
- [x] Report total physical memory
- [x] Report physical-memory percentage
- [x] Handle Win32 failures gracefully
- [x] Preserve monitoring-engine compatibility
- [x] Verify successful build
- [x] Verify successful runtime behavior

## Next

- [ ] Production disk monitoring
- [ ] Network throughput monitoring
- [ ] Process statistics and intelligence
- [ ] Microsoft Defender status
- [ ] Windows Firewall status
- [ ] Monitoring integration tests

---

# Roadmap

## Version 0.3

- Native CPU monitoring
- Native physical-memory monitoring
- CsWin32 integration
- Live dashboard updates
- Production system-monitor foundation

## Version 0.4

- Disk monitoring
- Network throughput
- Process intelligence
- Microsoft Defender integration
- Windows Firewall integration
- Event Log monitoring

## Version 0.5

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

1. Plan
2. Review repository usage and architecture
3. Implement
4. Build
5. Run
6. Verify
7. Update documentation
8. Commit
9. Push

The application should remain buildable and runnable throughout development.

---

# Build Requirements

- Windows 10 or Windows 11
- Visual Studio with the WinUI application-development workload
- .NET 8 SDK
- Windows App SDK dependencies restored through NuGet

---

# Engineering Philosophy

Every completed feature should be:

- Functional
- Stable
- Secure
- Tested
- Maintainable
- Documented
- Production-ready

Architecture and long-term maintainability take precedence over short-term implementation shortcuts.

---

# Documentation

The `docs` directory contains project planning, architecture, engineering, security, testing, and release documents.

Important references include:

- Project status
- Product requirements
- System architecture
- Coding standards
- Release checklist
- Development and implementation trackers

---

# Long-Term Vision

Sentinel AI is intended to become a professional Windows security platform capable of:

- Monitoring system health
- Detecting suspicious behavior
- Explaining security risks
- Providing AI-powered recommendations
- Assisting users with system maintenance
- Supporting enterprise environments

Rather than simply displaying technical information, Sentinel AI aims to help users understand their computers through intelligent analysis, transparent explanations, and actionable guidance.

---

# License

This project is currently under active development. Licensing terms will be finalized prior to the first public release.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.
