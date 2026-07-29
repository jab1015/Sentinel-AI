# SAI-005 — Product Roadmap

Version: 1.1

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This roadmap defines the planned evolution of Sentinel AI from its current development state into a commercial Windows security and system-intelligence application.

The roadmap is a living document. Actual completion status is controlled by runtime verification and the SAI-031 Implementation Tracker.

---

# Product Vision

Sentinel AI will become an AI-powered Windows security assistant that helps users understand, monitor, and improve the health and security of their computers.

Rather than overwhelming users with technical information, Sentinel AI will explain system activity in clear, actionable language.

---

# Development Strategy

Development proceeds through incremental, verified releases.

Every release must:

- Preserve a buildable and runnable application
- Replace placeholders with production data
- Avoid regressions in completed monitoring
- Produce visible user value
- Update project tracking documentation

---

# Version 0.1.0 — Foundation

Status: Completed

Objectives completed:

- WinUI 3 application
- Initial dashboard
- GitHub integration
- Core repository structure
- Development workflow

---

# Version 0.2.0 — Architecture and Documentation

Status: Completed

Objectives completed:

- MonitoringEngine
- SystemSnapshot
- Monitor-service architecture
- Live one-second refresh foundation
- Project documentation library
- Release and implementation tracking

---

# Version 0.3.0 — Native System Monitoring Foundation

Status: Completed and Runtime Verified

Objectives completed:

- Microsoft.Windows.CsWin32 integration
- Native CPU monitoring through `GetSystemTimes`
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- CPU and memory MonitoringEngine integration
- Real CPU dashboard display
- Real physical-memory dashboard display
- Live timestamp refresh
- Successful build and runtime verification

Success criteria achieved:

The dashboard displays accurate, live CPU and physical-memory information without placeholder or random values.

---

# Version 0.4.0 — Monitoring Expansion

Status: In Progress

Objectives:

- Display existing disk capacity, free-space, used-space, and usage-percentage data
- Implement live network download throughput
- Implement live network upload throughput
- Display process count
- Add process CPU and memory intelligence
- Implement Microsoft Defender operational status
- Implement Windows Firewall operational status
- Add monitoring integration tests
- Add failure-path testing

Current repository state:

- Disk service calculations exist and are collected by MonitoringEngine, but are not displayed by the dashboard
- Process count is collected by MonitoringEngine, but is not displayed by the dashboard
- Network throughput remains hardcoded to zero
- Defender and Firewall checks are not yet complete production status implementations

Success criteria:

The dashboard displays verified real-time disk, network, process, Defender, and Firewall information while preserving CPU and memory behavior.

---

# Version 0.5.0 — Security Intelligence and AI

Status: Planned

Objectives:

- Windows Event Log monitoring
- Startup application analysis
- Scheduled task inspection
- Threat analysis engine
- Risk scoring
- Confidence scoring
- AI-generated explanations
- Personalized security recommendations
- Explainable AI

Success criteria:

Sentinel AI can identify notable security conditions, explain why they matter, and recommend clear next actions.

---

# Version 0.6.0 — Protection, Reporting, and Notifications

Status: Planned

Objectives:

- Alert center
- Toast notifications
- Threat history
- Historical system reporting
- Performance analytics
- Security timeline
- Exportable reports

---

# Version 0.7.0 — Performance and Productivity

Status: Planned

Objectives:

- Startup optimization
- Storage recommendations
- Battery analysis
- Resource-usage history
- Scheduled health reports
- Long-term system scoring

---

# Version 0.8.0 — Enterprise Preparation

Status: Planned

Objectives:

- Multi-device support
- Policy management
- Central administration
- Cloud synchronization
- Enterprise reporting
- Logging and audit improvements

---

# Version 0.9.0 — Release Candidate

Status: Planned

Objectives:

- UI polish
- Accessibility review
- Performance optimization
- Installer completion
- Upgrade process
- Documentation review
- Beta testing
- Release-candidate acceptance testing

---

# Version 1.0.0 — Commercial Release

Status: Planned

Objectives:

- Production release
- MSIX installer
- Automatic updates
- Complete documentation
- User guide
- Licensing
- Release approval

Success criteria:

Sentinel AI is stable, secure, documented, installable, and approved for public distribution.

---

# Long-Term Ideas

Potential future capabilities include:

- Mobile companion application
- Browser extension
- AI malware analysis
- Vulnerability scanning
- Home-network discovery
- Smart-home security integration
- Plugin architecture
- Enterprise management console

These ideas are exploratory and are not current commitments.

---

# Product Principles

Every feature should improve at least one of the following:

- Security
- Understanding
- Performance
- Reliability
- Usability

A feature that does not support these goals should be reconsidered.

---

End of Document