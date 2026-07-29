# SAI-020 — Architecture Decision Record (ADR)

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document records significant architectural decisions made during the development of Sentinel AI.

Each Architecture Decision Record (ADR) captures the context, decision, rationale, consequences, and implementation status to preserve institutional knowledge and support future maintenance.

---

# ADR-001 — Layered Architecture

## Status

Accepted

## Decision

Sentinel AI will use a layered architecture:

```
User Interface
      │
Application Services
      │
Monitoring Engine
      │
Monitor Services
      │
Windows APIs
```

## Rationale

- Clear separation of responsibilities
- Easier maintenance
- Improved scalability
- Better testability

## Consequences

- Components remain loosely coupled.
- UI cannot directly access Windows APIs.

---

# ADR-002 — Monitoring Engine as Coordinator

## Status

Accepted

## Decision

The Monitoring Engine coordinates all monitor services and produces a unified `SystemSnapshot`.

## Rationale

- Single source of truth
- Simplified UI
- Consistent refresh cycle

## Consequences

- Monitor services remain independent.
- Future monitors can be added without modifying the UI.

---

# ADR-003 — Snapshot-Based Communication

## Status

Accepted

## Decision

Application layers exchange monitoring data using immutable snapshot models rather than direct monitor references.

## Rationale

- Simplifies data flow
- Reduces coupling
- Supports future serialization and reporting

## Consequences

- Components consume snapshots instead of querying monitors directly.

---

# ADR-004 — Native Windows APIs

## Status

Accepted

## Decision

Native Windows APIs are preferred over legacy APIs or unsupported techniques whenever practical.

## Rationale

- Accuracy
- Performance
- Long-term Microsoft support
- Better compatibility with Windows 11

## Consequences

- Additional interop code may be required.
- Native API wrappers should remain isolated within monitor services.

---

# ADR-005 — AI Consumes Snapshots Only

## Status

Accepted

## Decision

The AI Engine will never communicate directly with Windows APIs.

## Rationale

- Simplifies testing
- Decouples AI from platform-specific implementations
- Supports future portability

## Consequences

- AI operates exclusively on validated monitoring data.

---

# ADR-006 — Asynchronous Monitoring

## Status

Accepted

## Decision

Monitoring operations should execute asynchronously whenever practical.

## Rationale

- Responsive UI
- Improved scalability
- Better user experience

## Consequences

- Services should expose asynchronous APIs.
- Long-running operations must avoid blocking the UI thread.

---

# ADR-007 — Documentation as Part of Development

## Status

Accepted

## Decision

Major architectural or public-facing changes require corresponding documentation updates.

## Rationale

- Maintains alignment between implementation and documentation
- Simplifies onboarding
- Preserves design intent

## Consequences

- Documentation updates become part of the Definition of Done.

---

# Future ADRs

Future records may include decisions regarding:

- Dependency Injection
- Plugin Framework
- Logging Framework
- Configuration Management
- AI Model Integration
- Enterprise Features
- Update Infrastructure
- Cloud Services

---

# Long-Term Goal

Maintain a historical record of architectural decisions so future contributors understand why important technical choices were made, reducing rework and improving long-term maintainability.

---

End of Document