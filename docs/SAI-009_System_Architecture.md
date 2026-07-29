# SAI-009 — System Architecture

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the architectural blueprint for Sentinel AI.

It serves as the authoritative reference for how all major components interact and provides guidance for future development.

Architecture decisions documented here should minimize future rewrites while supporting long-term expansion into a commercial and enterprise-grade Windows security platform.

---

# Guiding Principles

- Security First
- Reliability Over Complexity
- Modular Design
- Separation of Responsibilities
- Native Windows Integration
- AI-Assisted Threat Detection
- Extensible Architecture
- Testable Components
- Performance Focused
- Production Quality

---

# High-Level Architecture

```
                User Interface
                       │
                       ▼
              Monitoring Engine
                       │
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
  System Monitor   Security Monitor   Process Monitor
        │              │              │
        ▼              ▼              ▼
  Disk Monitor    Network Monitor  Windows Info Monitor
        │
        ▼
  System Snapshot
        │
        ▼
     Dashboard UI
```

---

# Core Components

## User Interface

Responsibilities

- Dashboard
- Settings
- Alerts
- Notifications
- Reports
- Configuration

The UI should never directly communicate with Windows APIs.

All information flows through the Monitoring Engine.

---

## Monitoring Engine

Responsibilities

- Coordinate all monitor services
- Refresh monitoring data
- Create unified SystemSnapshot
- Publish updates to UI
- Future scheduling
- Event routing

The Monitoring Engine is the central hub of Sentinel AI.

---

## System Snapshot

The snapshot represents a complete view of the system at a single point in time.

Examples include:

- CPU usage
- Memory usage
- Disk usage
- Network status
- Process count
- Defender status
- Firewall status
- Timestamp

Future versions may include:

- GPU usage
- Battery status
- Windows Update state
- Installed security software
- Threat level
- AI confidence score

---

# Monitor Services

## SystemMonitor

Responsibilities

- CPU utilization
- Memory utilization
- System performance

Current Status

Prototype

Future

Native Windows implementation.

---

## DiskMonitor

Responsibilities

- Disk capacity
- Free space
- Used space
- Usage percentage

Future

- SMART status
- Disk health
- Temperature
- SSD wear
- Performance

---

## NetworkMonitor

Responsibilities

- Active adapters
- Connection status
- Link speed

Future

- Upload throughput
- Download throughput
- Packet loss
- DNS status
- Gateway monitoring
- Internet health

---

## ProcessMonitor

Responsibilities

- Running process count
- High memory processes

Future

- Process reputation
- Digital signatures
- Startup applications
- Resource analysis
- Suspicious process detection

---

## SecurityMonitor

Responsibilities

- Windows Defender detection
- Windows Firewall detection

Future

- Defender status
- Firewall profiles
- Real-time protection
- Controlled Folder Access
- Security Center integration

---

## WindowsInfoMonitor

Responsibilities

- Operating system
- Machine information
- User information
- Processor count
- System uptime

Future

- BIOS
- TPM
- Secure Boot
- Virtualization
- Windows edition

---

# Future Components

## Threat Analysis Engine

Responsibilities

- Threat scoring
- Behavioral analysis
- Suspicious activity detection
- Machine learning inference

---

## Rules Engine

Responsibilities

- User rules
- Enterprise policies
- Automated actions

---

## Event Pipeline

Responsibilities

- Event aggregation
- Event normalization
- Alert generation

---

## Logging Engine

Responsibilities

- Application logs
- Audit logs
- Security logs

---

## Notification Engine

Responsibilities

- Toast notifications
- Email alerts
- Enterprise notifications

---

## Update Engine

Responsibilities

- Application updates
- Rule updates
- AI model updates

---

## Plugin Framework

Future support for:

- Third-party integrations
- Custom monitors
- Enterprise extensions

---

# Data Flow

```
Windows APIs
      │
      ▼
Individual Monitors
      │
      ▼
Monitoring Engine
      │
      ▼
System Snapshot
      │
      ▼
Dashboard
```

---

# Architectural Rules

- UI never accesses Windows APIs directly.
- All monitoring flows through the Monitoring Engine.
- Monitor classes remain independent.
- Snapshot objects remain immutable once published.
- Business logic never resides inside the UI.
- New features should integrate through the Monitoring Engine whenever possible.
- Native Windows APIs are preferred over legacy APIs where appropriate.
- Placeholder implementations should be replaced with production implementations before release.

---

# Current Status

Completed

- Monitoring Engine
- System Snapshot
- System Monitor
- Disk Monitor
- Network Monitor
- Process Monitor
- Security Monitor
- Windows Info Monitor

In Progress

- Native CPU monitoring
- Physical memory monitoring
- Network throughput
- Defender integration

Planned

- Firewall integration
- Event Log monitoring
- Threat Analysis Engine
- AI Engine
- Rules Engine
- Logging Engine
- Plugin Framework
- Enterprise Features

---

# Long-Term Vision

Sentinel AI will evolve from a desktop monitoring application into a comprehensive AI-powered Windows security platform capable of:

- Real-time monitoring
- Threat detection
- AI-assisted analysis
- Automated remediation
- Enterprise management
- Cloud synchronization
- Security reporting
- Extensible plugin ecosystem

---

End of Document