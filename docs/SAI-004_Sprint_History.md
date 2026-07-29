# SAI-004 — Sprint History

Version: 1.2  
Status: Active  
Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the official development log for Sentinel AI.

---

# Completed Sprints

## Sprint 1 — Foundation

- Created the WinUI 3 solution and application
- Established the initial dashboard and branding
- Added live DispatcherTimer updates
- Established the repository and `main` branch

## Sprint 2 — Architecture and Documentation

- Created the monitoring-service architecture
- Added MonitoringEngine and SystemSnapshot
- Created the project documentation and tracking system
- Established coding, release, and development standards

## Sprint 3 — Native Windows Monitoring

Status: Completed and verified

Achievements:

- Added Microsoft.Windows.CsWin32
- Added `NativeMethods.txt`
- Implemented CPU monitoring with `GetSystemTimes`
- Implemented physical-memory monitoring with `GlobalMemoryStatusEx`
- Removed placeholder and random CPU data
- Connected CPU and memory to the live dashboard
- Verified successful build and runtime behavior

## Sprint 4 — Core Monitoring Expansion

Status: Completed and verified

Achievements:

- Connected disk capacity and usage to the dashboard
- Implemented live network download and upload throughput
- Added running process count
- Added highest-memory process identification and usage
- Implemented Microsoft Defender enabled status
- Implemented Windows Firewall enabled status
- Added security status to the dashboard
- Preserved one-second refresh behavior
- Verified successful build, launch, and live operation

---

# Active Sprint

## Sprint 5 — Security Intelligence Foundation

Status: Active

Objectives:

- Windows Event Log monitoring
- Critical and security event classification
- Suspicious-process indicators
- Startup application monitoring
- Service-health monitoring
- Integration and failure-path tests
- Alerting foundation

---

# Lessons Learned

- Replace placeholders incrementally and verify each dashboard feature at runtime.
- Preserve existing working features during every expansion.
- Repository documentation must be synchronized immediately after verification.
- Service-level capability is not complete until it is displayed and verified.

---

End of Document
