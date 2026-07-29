# SAI-011 — Coding Architecture

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the software engineering architecture and coding principles used throughout Sentinel AI.

It establishes mandatory development rules to ensure the codebase remains maintainable, scalable, testable, and production-ready.

---

# Core Principles

- Architecture before implementation.
- Simplicity over cleverness.
- Readability over brevity.
- Security first.
- Performance matters.
- Native Windows integration whenever practical.
- Every component has a single responsibility.

---

# Layered Architecture

```
Presentation Layer
        │
        ▼
Application Layer
        │
        ▼
Monitoring Engine
        │
        ▼
Monitor Services
        │
        ▼
Windows APIs
```

Dependencies always flow downward.

---

# Project Organization

```
Sentinel.App

├── Models
├── Services
├── Views
├── Helpers
├── Resources
├── Assets
├── Configuration
└── Extensions
```

Repository-level documentation remains outside the application project.

---

# Models

Models represent data only.

Models should:

- Contain no business logic.
- Be lightweight.
- Represent a snapshot of information.
- Be serializable when practical.

Example:

- SystemSnapshot
- SystemStatus

---

# Services

Services perform work.

Examples:

- MonitoringEngine
- DiskMonitor
- NetworkMonitor
- ProcessMonitor
- SecurityMonitor

Rules

- One responsibility per service.
- Avoid dependencies between monitor services.
- Return strongly typed objects whenever practical.

---

# Monitoring Engine

Responsibilities

- Coordinate monitors.
- Refresh data.
- Build snapshots.
- Publish updates.
- Handle scheduling.

The Monitoring Engine is the only component that communicates with all monitor services.

---

# User Interface

The UI is responsible only for presentation.

The UI must never:

- Access Windows APIs.
- Query hardware.
- Contain business rules.
- Perform monitoring.

The UI consumes snapshots.

---

# Error Handling

Every public service should:

- Catch expected exceptions.
- Return safe defaults.
- Log failures when logging becomes available.
- Never crash the application.

---

# Threading

Long-running operations should:

- Execute asynchronously.
- Avoid blocking the UI thread.
- Use async/await where appropriate.

---

# Windows Integration

Preferred order:

1. Native Windows APIs
2. Windows Runtime APIs
3. Official Microsoft libraries
4. .NET APIs

Avoid legacy APIs unless required.

---

# Dependency Rules

Allowed

UI

↓

Monitoring Engine

↓

Services

↓

Windows APIs

Not Allowed

UI

↓

Windows APIs

or

Monitor

↓

UI

---

# Naming Standards

Classes

PascalCase

Methods

PascalCase

Properties

PascalCase

Private fields

_prefixCamelCase

Constants

PascalCase

Interfaces

Prefix with I

---

# Documentation Standards

Every public class should contain:

- Purpose
- Responsibilities
- Authoritative comments where necessary

Avoid excessive comments describing obvious code.

---

# Future Architecture

Planned additions

- Dependency Injection
- Configuration Service
- Plugin Framework
- Logging Framework
- AI Engine
- Threat Analysis Engine
- Rules Engine
- Enterprise Services

---

# Quality Goals

Sentinel AI code should be:

- Easy to understand
- Easy to test
- Easy to extend
- Secure
- Stable
- Production-ready

---

End of Document