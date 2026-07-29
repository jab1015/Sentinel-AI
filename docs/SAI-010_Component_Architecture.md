# SAI-010 — Component Architecture

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the internal software components of Sentinel AI and the responsibilities of each component.

It serves as the primary reference for future development and ensures every subsystem has a clearly defined responsibility.

---

# Component Hierarchy

```
Sentinel AI

├── User Interface
│
├── Monitoring Engine
│   ├── System Monitor
│   ├── Disk Monitor
│   ├── Network Monitor
│   ├── Process Monitor
│   ├── Security Monitor
│   ├── Windows Info Monitor
│   └── Future Monitors
│
├── AI Engine
│
├── Threat Analysis Engine
│
├── Rules Engine
│
├── Logging Engine
│
├── Notification Engine
│
├── Reporting Engine
│
├── Update Engine
│
└── Plugin Framework
```

---

# User Interface

Responsibilities

- Dashboard
- Settings
- Alerts
- Reports
- Configuration
- User interaction

Rules

- Never communicate directly with Windows APIs.
- Never contain business logic.
- Display information only.

---

# Monitoring Engine

Responsibilities

- Coordinate monitors
- Refresh monitoring data
- Build SystemSnapshot
- Publish updates
- Handle refresh scheduling

Dependencies

- Monitor Services

---

# Monitor Services

Each monitor has one responsibility.

Current monitors

- SystemMonitor
- DiskMonitor
- NetworkMonitor
- ProcessMonitor
- SecurityMonitor
- WindowsInfoMonitor

Future monitors

- GPU Monitor
- Battery Monitor
- Service Monitor
- Registry Monitor
- Event Log Monitor
- Firewall Monitor
- Defender Monitor
- Device Monitor

---

# AI Engine

Responsibilities

- AI reasoning
- Threat classification
- Behavioral analysis
- Recommendations
- Confidence scoring

---

# Threat Analysis Engine

Responsibilities

- Analyze monitor output
- Correlate suspicious behavior
- Generate threat score
- Recommend actions

---

# Rules Engine

Responsibilities

- User-defined rules
- Enterprise policies
- Automatic responses
- Alert conditions

---

# Logging Engine

Responsibilities

- Application logs
- Security logs
- Audit logs
- Diagnostic logs

---

# Notification Engine

Responsibilities

- Toast notifications
- Critical alerts
- Email notifications
- Enterprise notifications

---

# Reporting Engine

Responsibilities

- Health reports
- Threat reports
- System summaries
- Export functionality

---

# Update Engine

Responsibilities

- Application updates
- AI model updates
- Rule updates
- Signature updates

---

# Plugin Framework

Future support for

- Third-party plugins
- Enterprise modules
- Custom monitors
- External integrations

---

# Design Rules

- Single responsibility per component.
- Loose coupling.
- High cohesion.
- Dependency injection where practical.
- Monitoring Engine coordinates all monitoring.
- UI consumes snapshots only.
- AI consumes snapshots only.
- Windows APIs remain isolated inside monitor services.

---

# Current Implementation

Completed

- Monitoring Engine
- Snapshot Model
- Core Monitor Services

Planned

- Native Windows integrations
- AI Engine
- Threat Analysis
- Reporting
- Notifications
- Enterprise Components

---

End of Document