# Sentinel AI

> **AI-Powered Windows Security & System Intelligence**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It combines real-time Windows system monitoring, security posture reporting, and a future security-intelligence layer in a clear dashboard.

---

# Project Status

**Version:** 0.4.0  
**Status:** Active Development  
**Completed Sprint:** Sprint 4 — Core Monitoring Expansion  
**Current Sprint:** Sprint 5 — Security Intelligence Foundation

The core dashboard milestone has been built and verified successfully by the Product Owner.

---

# Verified Production Features

- Native CPU monitoring through `GetSystemTimes`
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- System-drive used, total, and percentage reporting
- Live network download and upload throughput
- Running process count
- Highest-memory process identification and memory usage
- Microsoft Defender enabled status
- Windows Firewall enabled status
- One-second dashboard refresh
- Live timestamp updates
- Graceful unavailable-state handling
- Successful build, launch, and runtime verification

---

# Dashboard Status

| Component | Status |
|---|---|
| CPU | Complete and verified |
| Memory | Complete and verified |
| Disk | Complete and verified |
| Network | Complete and verified |
| Processes | Complete and verified |
| Microsoft Defender | Complete and verified |
| Windows Firewall | Complete and verified |
| Dashboard refresh | Complete and verified |

---

# Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 8 |
| UI Framework | WinUI 3 |
| Native API Generation | Microsoft.Windows.CsWin32 |
| Target Platform | Windows 10 and later |
| Version Control | Git |
| Production Branch | `main` |
| Build System | MSBuild |

---

# Architecture

```text
User Interface
      │
Monitoring Engine
      │
System Snapshot
      │
Monitor Services
      │
Windows and .NET System APIs
```

Core principles:

- Single responsibility
- Snapshot-based communication
- Native Windows integration
- Graceful failure handling
- Maintainable service boundaries
- Preserve working features during expansion

---

# Sprint 4 — Complete

- [x] Display production disk metrics
- [x] Verify disk values at runtime
- [x] Implement network download throughput
- [x] Implement network upload throughput
- [x] Display process count
- [x] Display highest-memory process
- [x] Implement Microsoft Defender enabled status
- [x] Implement Windows Firewall enabled status
- [x] Verify successful build and application launch
- [x] Verify all dashboard values at runtime

---

# Sprint 5 — Active

Priority order:

1. Add Windows Event Log monitoring
2. Add critical and security event classification
3. Add suspicious-process indicators
4. Add startup application monitoring
5. Add service-health monitoring
6. Add integration and failure-path tests
7. Introduce alerting and notification foundations
8. Prepare explainable security recommendations

---

# Roadmap

## Version 0.4 — Core Monitoring

Complete:

- CPU, memory, disk, network, and process monitoring
- Defender and Firewall status
- Live dashboard integration

## Version 0.5 — Security Intelligence

- Event Log intelligence
- Suspicious process indicators
- Startup and service analysis
- Threat classification
- Risk and confidence scoring

## Version 0.6 — Recommendations and Alerts

- Explainable recommendations
- Notification center
- Historical reporting
- Security timeline

## Version 1.0 — Commercial Release

- Production installer
- Automatic updates
- Complete testing and documentation
- Public-release readiness

---

# Development Workflow

1. Review project status and implementation tracker
2. Review repository call sites and architecture
3. Implement
4. Build
5. Run
6. Verify
7. Update documentation
8. Commit and push to `main`

The application must remain buildable and runnable throughout development.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.
