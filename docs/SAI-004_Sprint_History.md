# SAI-004 — Sprint History

Version: 1.1

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the official development log for Sentinel AI.

Every completed sprint must be recorded here, and the next active sprint must be identified before development continues.

---

# Version History

## Version 0.1.0 — Foundation

Status: Completed

### Sprint 1.0 — Environment and First Build

Status: Completed

Achievements:

- Installed Visual Studio and WinUI development tools
- Created the Sentinel AI solution
- Successfully compiled the project
- Successfully launched the first application

### Sprint 1.1 — Initial Dashboard

Status: Completed

Achievements:

- Created the first dashboard
- Added dark theme and application branding
- Added the System Status panel

### Sprint 1.2 — Live Refresh Foundation

Status: Completed

Achievements:

- Added live UI updates using DispatcherTimer
- Connected the dashboard to application logic
- Verified successful builds and execution

### Sprint 1.3 — Service Organization

Status: Completed

Achievements:

- Created the Services folder
- Created the initial SystemMonitor class
- Established project organization

### Sprint 1.4 — Version Control

Status: Completed

Achievements:

- Initialized Git
- Connected the GitHub repository
- Established `main` as the production branch
- Established the version-control workflow

---

## Version 0.2.0 — Architecture and Documentation Foundation

Status: Completed

### Sprint 2.0 — Project Documentation

Status: Completed

Achievements:

- Created the project documentation system
- Created project status, architecture, roadmap, coding, workflow, testing, and release documents
- Established the release checklist and implementation tracker

### Sprint 2.1 — Monitoring Architecture

Status: Completed

Achievements:

- Created MonitoringEngine
- Created SystemSnapshot
- Added monitor-service boundaries
- Connected the dashboard refresh loop to MonitoringEngine
- Preserved a buildable and runnable application

---

## Version 0.3.0 — Native Windows Monitoring

Status: Completed

### Sprint 3 — Production CPU and Physical Memory

Status: Completed and Runtime Verified

Achievements:

- Added Microsoft.Windows.CsWin32
- Enabled generated pointer-based Windows API access
- Added `NativeMethods.txt`
- Added `GetSystemTimes`
- Added `GlobalMemoryStatusEx`
- Replaced placeholder and random CPU values
- Implemented consecutive CPU sampling
- Added first-sample and invalid-sample handling
- Clamped CPU utilization to 0–100 percent
- Implemented physical-memory used, total, and percentage reporting
- Preserved the existing SystemMonitor interface
- Connected native CPU and memory values to MonitoringEngine
- Displayed real CPU and physical-memory values on the dashboard
- Verified one-second refresh and timestamp updates
- Completed a successful build
- Successfully launched and runtime-verified the application

Additional repository findings recorded during Sprint 3 closeout:

- DiskMonitor already calculates system-drive total, free, used, and usage percentage
- MonitoringEngine already collects disk values
- ProcessMonitor already supplies process count to MonitoringEngine
- The dashboard currently renders only CPU, memory, and timestamp
- Network upload and download values remain zero placeholders
- Defender and Firewall monitoring remain incomplete production checks

---

## Version 0.4.0 — Monitoring Expansion

Status: In Progress

### Sprint 4 — Disk, Network, Process, and Security Status

Status: Active

Objectives:

1. Bind existing disk metrics to the dashboard
2. Verify disk used, total, free, and percentage values at runtime
3. Implement live download and upload throughput
4. Display process count and expand process intelligence
5. Implement Microsoft Defender operational status
6. Implement Windows Firewall operational status
7. Add monitoring integration tests
8. Add unavailable-service and failure-path tests

Acceptance criteria:

- Existing CPU and memory monitoring continue to work without regression
- New metrics display real values rather than placeholders
- The project builds successfully
- The application launches successfully
- Runtime behavior is verified
- Tracking documents are synchronized

---

# Future Versions

## Version 0.5.0 — Security Intelligence and AI

Status: Planned

Planned capabilities:

- Threat analysis
- Risk scoring
- Explainable recommendations
- Security-event interpretation
- Confidence scoring

## Version 0.6.0 — Reporting and Notifications

Status: Planned

Planned capabilities:

- Notification center
- Historical reporting
- Performance analytics
- Security timeline

## Version 1.0.0 — Commercial Release

Status: Planned

Planned capabilities:

- MSIX installer
- Automatic updates
- Complete documentation
- Release testing
- Production approval

---

# Lessons Learned

- The repository and runtime must both be reviewed before declaring a feature complete.
- Service implementation and dashboard display are separate completion gates.
- Placeholder values must be distinguished from real but undisplayed service data.
- Complete-file updates reduce merge and formatting errors.
- Documentation must be synchronized immediately after runtime verification.
- The production branch must remain `main`.

---

# Next Sprint Action

Begin Sprint 4 with disk dashboard binding and runtime verification.

---

End of Document