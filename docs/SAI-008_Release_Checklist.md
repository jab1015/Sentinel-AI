# SAI-008 — Release Checklist

Version: 1.2  
Status: Active  
Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This checklist defines the minimum requirements for completing a development session, feature, sprint, or release.

A task is not complete until applicable implementation, build, verification, documentation, and version-control requirements are satisfied.

---

# Standard Quality Gate

## Planning

☐ Requirement understood  
☐ Architecture and existing code reviewed  
☐ Dependencies, risks, and regression impact considered  
☐ Implementation plan completed

## Development

☐ Code implemented  
☐ SAI-006 Coding Standards followed  
☐ Error handling added  
☐ Temporary and placeholder code removed or documented  
☐ Existing architecture preserved

## Build and Verification

☐ Solution builds successfully  
☐ Application launches successfully  
☐ Feature behaves as expected  
☐ Existing functionality still works  
☐ Manual smoke test completed  
☐ Error scenarios tested when applicable

## Documentation and Version Control

☐ Tracking documents updated  
☐ Files reviewed  
☐ Meaningful commit created  
☐ Changes pushed to `main`  
☐ Repository synchronized

---

# Sprint 3 — Native CPU and Memory Verification

☑ Microsoft.Windows.CsWin32 configured  
☑ `NativeMethods.txt` created  
☑ `GetSystemTimes` generated and used  
☑ `GlobalMemoryStatusEx` generated and used  
☑ Placeholder CPU values removed  
☑ Native CPU monitoring verified  
☑ Native physical-memory monitoring verified  
☑ Dashboard integration verified  
☑ Build and runtime verification completed

---

# Sprint 4 — Core Monitoring Expansion Verification

## Disk

☑ System drive detected  
☑ Total, free, used, and percentage values calculated  
☑ Disk metrics connected to MonitoringEngine  
☑ Disk metrics displayed on the dashboard  
☑ Runtime values verified by the Product Owner

## Network

☑ Active network data sampled  
☑ Download throughput implemented  
☑ Upload throughput implemented  
☑ Network metrics connected to MonitoringEngine  
☑ Network metrics displayed on the dashboard  
☑ Runtime values verified by the Product Owner

## Processes

☑ Running process count implemented  
☑ Highest-memory process identified  
☑ Highest-memory process usage reported  
☑ Process metrics connected to MonitoringEngine  
☑ Process metrics displayed on the dashboard  
☑ Runtime values verified by the Product Owner

## Windows Security

☑ Microsoft Defender enabled status implemented  
☑ Windows Firewall enabled status implemented  
☑ Graceful unavailable states supported  
☑ Security values connected to MonitoringEngine  
☑ Security status displayed on the dashboard  
☑ Runtime values verified by the Product Owner

## Final Verification

☑ CPU, memory, disk, network, process, Defender, and Firewall values displayed  
☑ One-second refresh preserved  
☑ Timestamp updates correctly  
☑ Solution build completed successfully  
☑ Application launched successfully  
☑ No visible regression in completed monitoring  
☑ Product Owner accepted the completed core dashboard  
☑ README and project tracking synchronized

---

# Sprint 5 — Remaining Work

☐ Windows Event Log monitoring  
☐ Critical and security event classification  
☐ Suspicious-process indicators  
☐ Startup application monitoring  
☐ Service-health monitoring  
☐ Monitoring integration tests  
☐ Failure-path and unavailable-service tests  
☐ Alerting foundation

---

# Definition of Done

A feature is complete only when requirements are satisfied, implementation is present, the application builds and runs, runtime behavior is verified, existing functionality is preserved, documentation is current, and changes are pushed to `main`.

---

End of Document
