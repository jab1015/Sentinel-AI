# SAI-024 — Project Glossary

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This glossary defines the terminology used throughout the Sentinel AI project to ensure consistent communication across engineering, documentation, testing, and future product development.

---

# Definitions

## AI Engine

The subsystem responsible for analyzing monitoring data, identifying patterns, assigning threat levels, and generating recommendations.

---

## Alert

A notification generated when Sentinel AI detects a condition that requires user attention.

Alerts may be:

- Informational
- Warning
- Critical

---

## Architecture

The overall structure of Sentinel AI, including its components, responsibilities, and relationships.

---

## Component

A logical unit of software responsible for a specific function.

Examples include:

- Monitoring Engine
- Rules Engine
- Notification Engine

---

## Dashboard

The primary user interface displaying system health, monitoring information, alerts, and recommendations.

---

## Monitor Service

A service responsible for collecting one category of system information.

Examples:

- DiskMonitor
- NetworkMonitor
- ProcessMonitor
- SecurityMonitor

---

## Monitoring Engine

The central coordinator responsible for refreshing monitor services and producing a unified SystemSnapshot.

---

## Native Windows API

Microsoft-supported Windows operating system interfaces used to retrieve system information.

---

## Recommendation

An action suggested by the AI Engine based on monitoring results and threat analysis.

---

## Rule

A condition evaluated by the Rules Engine to determine whether an alert or automated response should occur.

---

## Snapshot

A strongly typed representation of the current system state.

Current implementation:

SystemSnapshot

---

## System Health

An overall assessment of the operating condition of the monitored computer.

---

## Telemetry

Collected monitoring information describing system behavior and status.

---

## Threat

Any condition indicating possible security, performance, or reliability concerns.

---

## Threat Score

A numerical or categorical assessment representing the severity of detected activity.

---

## User Interface (UI)

The WinUI desktop application presented to the user.

The UI displays information but does not directly access Windows APIs.

---

## Windows API

Official Microsoft programming interfaces used by monitor services to retrieve operating system information.

---

## Windows Security Center

The Windows subsystem responsible for reporting antivirus, firewall, and related security status.

---

# Acronyms

| Acronym | Meaning |
|----------|---------|
| ADR | Architecture Decision Record |
| AI | Artificial Intelligence |
| API | Application Programming Interface |
| CI | Continuous Integration |
| CPU | Central Processing Unit |
| DI | Dependency Injection |
| GPU | Graphics Processing Unit |
| MSIX | Microsoft Installer Package |
| SDK | Software Development Kit |
| UI | User Interface |
| WinRT | Windows Runtime |
| WMI | Windows Management Instrumentation |

---

# Document Usage

This glossary serves as the authoritative reference for project terminology.

Future documentation should use these definitions consistently.

New terms should be added as the project evolves.

---

End of Document