# SAI-025 — Master Development Plan

Version: 1.3

Status: Active

Last Updated: 2026-07-31

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document serves as the master engineering plan for Sentinel AI and consolidates the project vision, architecture, implementation strategy, development phases, quality objectives, remaining work, and release milestones.

---

# Project Mission

Develop a production-quality Windows investigation and security application that combines:

- Native Windows monitoring
- Investigation and evidence correlation
- Explainable security intelligence
- Safe remediation
- Autonomous low-risk protection
- Actionable recommendations
- Professional, non-technical user experience

---

# Current Status

Estimated overall product completion: **85%**.

Completed:

- Project architecture and documentation framework
- Monitoring Engine and snapshot architecture
- Native CPU, memory, disk, and network monitoring
- Process, service, security, persistence, scheduled-task, connection, and Windows Event evidence
- Investigation-first dashboard and healthy-state UX
- Investigation Engine core — 18 of 18
- Risk assessment and guidance foundations
- Safe Remediation Foundation — 10 of 10
- Verified process termination foundation
- Verified outbound firewall blocking foundation
- Quarantine and hash-verified restore foundation
- Remediation audit and investigation history foundations
- Autonomous Protection core — 10 of 10
- Low-risk automatic action gating
- Evidence-confidence and execution-time revalidation safeguards
- User-approval boundary for moderate/high-risk remediation
- Recurrence-aware investigation and escalation safeguards
- Startup/refresh responsiveness improvements
- First-refresh CPU sampling improvement
- Per-Windows-profile preferred-name onboarding
- Sustained memory-pressure investigation with application contributor context and actionable guidance

In Progress / Next:

1. User-facing approval workflow for moderate-risk remediation.
2. Quarantine management and restore experience.
3. Remediation and investigation history presentation.
4. Network endpoint attribution and response improvements.
5. Background actionable notifications.
6. Failure-path and remediation integration regression testing.
7. Continued startup and investigation performance profiling; intermittent lag remains observable in recent runtime verification.

Planned after Phase 5:

- Ask Sentinel / AI Assistance
- Production hardening and structured diagnostics
- Fresh-clone and release configuration verification
- Automated regression coverage
- Long-duration stability testing
- Windows 10 and Windows 11 compatibility verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy/user/troubleshooting documentation
- Accessibility and UX polish
- Release acceptance testing
- Enterprise functionality after commercial release foundation

---

# Development Phases

## Phase 1 — Monitoring Foundation

Status: **Complete**

Native monitoring and core Windows evidence collection are operational and locally verified.

---

## Phase 2 — Investigation Experience

Status: **Complete**

Sentinel defaults to a calm healthy-state experience, exposes technical details progressively, and interrupts the user only when investigation evidence justifies attention.

---

## Phase 3 — Investigation Engine

Status: **Complete — 18 of 18**

Evidence collection and correlation foundations span processes, services, persistence, scheduled tasks, connections, drivers, firewall, WMI, ancestry, command lines, Windows events, sustained memory-pressure investigation, and multi-signal correlation.

---

## Phase 4 — Safe Remediation Foundation

Status: **Complete — 10 of 10**

System-changing actions are governed by centralized policy, evidence requirements, approval boundaries, protected-component safeguards, verification, and audit/history foundations.

---

## Phase 5 — Remediation Integration & Autonomous Protection

Status: **Autonomous Protection core complete — 10 of 10; remaining integration active**

Completed capabilities include low-risk automatic remediation decisions, policy enforcement, confidence gating, execution isolation, stale-decision revalidation, safe refresh/retry actions, verification-pending outcomes, recurrence-aware escalation safeguards, and memory-pressure guidance that identifies application contributors rather than treating Windows Memory Compression itself as the problem.

Remaining integration work:

1. User-facing approval workflow for supported moderate-risk actions.
2. Quarantine management and safe restore presentation.
3. Remediation and investigation history presentation without cluttering healthy state.
4. Expanded network endpoint attribution and response.
5. Background/minimized notification behavior for genuinely actionable findings.
6. Failure-path and remediation integration regression testing.
7. Continued startup/initial-investigation performance profiling and optimization.

---

## Phase 6 — Ask Sentinel / AI Assistance

Status: **Planned**

Objectives:

- Natural-language questions grounded in current evidence
- Investigation-history-aware explanations
- Verified system-state answers
- Explainable recommendations
- No unsupported claims or invented system state

---

## Phase 7 — Production Hardening & Commercial Release

Status: **Planned / partially underway**

Objectives:

- Structured logging and diagnostics
- Fresh-clone build verification
- Release configuration verification
- Automated regression coverage
- Performance profiling and optimization
- One-hour and eight-hour stability testing
- Windows 10 and Windows 11 compatibility verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy, user, and troubleshooting documentation
- Accessibility and UX polish
- Release acceptance testing

---

# Current Performance Note

Recent local runtime verification confirms successful builds and correct monitoring behavior. Intermittent lag is still observable during startup/initial investigation on some runs. This is a tracked Phase 5/production-hardening item. Optimization must preserve evidence quality, remediation safeguards, and the responsive shell-first startup behavior already implemented.

---

# Progress Governance

**85% is the synchronized overall project baseline as of 2026-07-31.** SAI-012 Product Roadmap, SAI-013 Implementation Tracker, this Master Development Plan, and README must report the same overall progress and phase state.

Future progress reports must not reduce the overall percentage unless previously completed functionality is explicitly reopened, removed, or proven incomplete. Any such reduction must be documented with the affected scope and reason.

---

# Engineering Priorities

1. Preserve investigation-before-action behavior and safe remediation boundaries.
2. Maintain a fast, calm user experience that does not overwhelm non-technical users.
3. Verify system-changing outcomes before reporting success.
4. Maintain clean architecture and synchronized documentation.
5. Ensure every sprint results in a buildable, runnable application.

---

# Quality Objectives

Sentinel AI shall be stable, secure, fast, explainable, conservative with system changes, maintainable, extensible, and testable.

---

# Technical Debt Policy

Technical debt should be documented, prioritized, never accumulate silently, and be resolved before major releases whenever practical.

---

# Sprint Workflow

Each sprint follows:

1. Planning
2. Architecture review
3. Implementation
4. Build
5. Run
6. Verification
7. Documentation
8. Commit
9. Push

No sprint is complete until all steps are finished successfully.

---

# Definition of Success

A successful release:

- Builds without errors
- Runs successfully
- Meets acceptance criteria
- Has updated documentation
- Passes verification
- Preserves remediation safety boundaries
- Does not report unverified remediation as successful
- Is committed and versioned

---

# Long-Term Vision

Sentinel AI will evolve into a trusted Windows investigation and security platform that combines native operating-system evidence, explainable intelligence, and carefully governed autonomous protection so users can understand and respond to threats without being overwhelmed by technical complexity.

---

# Related Documents

- SAI-009 — System Architecture
- SAI-010 — Component Architecture
- SAI-011 — Coding Architecture
- SAI-012 — Product Roadmap
- SAI-013 — Implementation Tracker
- SAI-014 — Development Workflow
- SAI-015 — Contribution Guide
- SAI-016 — Testing Strategy
- SAI-017 — Release Management
- SAI-018 — Deployment Guide
- SAI-019 — Security Architecture
- SAI-020 — Architecture Decision Record
- SAI-021 — Project Standards
- SAI-022 — Product Vision
- SAI-023 — Technology Stack
- SAI-024 — Project Glossary

---

End of Document