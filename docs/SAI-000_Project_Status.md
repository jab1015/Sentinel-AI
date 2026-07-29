# SAI-000 — Project Status

Version: 1.1

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the single source of truth for the current state of the Sentinel AI project.

Every development session should begin by reviewing this file.

---

# Project Information

**Project Name:** Sentinel AI  
**Description:** AI-powered Windows desktop security and system-intelligence application  
**Platform:** Windows Desktop  
**Framework:** WinUI 3  
**Language:** C#  
**Runtime:** .NET 8  
**Native API Generation:** Microsoft.Windows.CsWin32  
**Repository:** GitHub  
**Production Branch:** main

---

# Current Version

0.3.0

---

# Current Development State

Sprint 3 — Native Windows Monitoring is complete.

The application builds and launches successfully. The dashboard refreshes once per second and displays verified production CPU and physical-memory values.

## Verified Production Capabilities

- Native CPU monitoring through `GetSystemTimes`
- Consecutive CPU sampling with first-sample handling
- CPU result validation and 0–100 percent clamping
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- Used, total, and percentage physical-memory reporting
- CsWin32-generated Windows API bindings
- MonitoringEngine integration
- One-second dashboard refresh
- Live dashboard timestamp
- Successful build and runtime verification by the Product Owner

## Implemented but Not Yet Displayed as Production Dashboard Metrics

- System-drive capacity
- System-drive free space
- System-drive usage percentage
- Running process count

## Remaining Placeholder or Incomplete Areas

- Disk dashboard binding and runtime verification
- Network download throughput
- Network upload throughput
- Process statistics and process intelligence
- Microsoft Defender operational status
- Windows Firewall operational status
- Event Log monitoring
- Monitoring integration tests
- Error-path and unavailable-service tests

---

# Current Sprint

Sprint 4 — Monitoring Expansion

## Primary Objective

Complete and display the remaining production monitoring metrics without regressing the verified CPU and memory implementation.

## Priority Order

1. Bind and verify disk metrics in the dashboard
2. Implement live network download and upload throughput
3. Expand and display process statistics
4. Implement Microsoft Defender status
5. Implement Windows Firewall status
6. Add monitoring integration and failure-path tests

---

# Completed Work

## Foundation

- [x] WinUI 3 application created
- [x] .NET 8 project established
- [x] Solution builds successfully
- [x] Application launches successfully
- [x] GitHub repository connected
- [x] Production branch established as `main`

## Architecture

- [x] MonitoringEngine created
- [x] SystemSnapshot model created
- [x] Monitor-service architecture created
- [x] DispatcherTimer refresh loop implemented
- [x] CsWin32 integrated
- [x] `NativeMethods.txt` added

## Native System Monitoring

- [x] Production CPU monitoring
- [x] Production physical-memory monitoring
- [x] CPU dashboard display
- [x] Memory dashboard display
- [x] Live timestamp display
- [x] Disk capacity/free-space service logic
- [x] Process-count service logic

## Documentation

- [x] Project status
- [x] Sprint history
- [x] Product roadmap
- [x] Release checklist
- [x] Implementation tracker
- [x] README
- [x] Changelog

---

# Immediate Next Task

Connect the existing disk snapshot values to the dashboard, display used/total/percentage information, build, run, and verify the result before beginning network throughput monitoring.

---

# Known Limitations

- `MainWindow.xaml.cs` currently updates only CPU, memory, and timestamp text.
- MonitoringEngine currently sets download and upload values to zero.
- Disk service values are collected but not displayed by the current dashboard code.
- Process count is collected but not displayed by the current dashboard code.
- Existing security methods do not yet provide complete production Defender and Firewall status intelligence.

---

# Definition of Done

A feature is complete only when:

- Requirements are satisfied.
- Code builds successfully.
- The application launches successfully.
- Runtime behavior is verified.
- Existing functionality remains working.
- Documentation is synchronized.
- Changes are committed and pushed to `main`.

---

# Supporting Documentation

- SAI-004 — Sprint History
- SAI-005 — Product Roadmap
- SAI-008 — Release Checklist
- SAI-031 — Implementation Tracker
- CHANGELOG.md
- README.md

---

End of Document