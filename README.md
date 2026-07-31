# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language investigation conclusions only when attention is warranted, and provides policy-controlled remediation and autonomous protection foundations.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Phase 5 — Remediation Integration & Autonomous Protection  
**Autonomous Protection Core:** **Complete — 10 of 10**  
**Estimated Overall Product Progress:** **80%**

The application builds, launches, monitors the system continuously, suppresses non-actionable findings, investigates sustained memory pressure with application-level context, and has been repeatedly runtime-verified by the Product Owner.

A remaining performance item is intermittent startup/initial-investigation lag observed during recent runtime verification. This is tracked for continued profiling and hardening and does not block the current successful builds.

---

# Overall Progress

| Area | Status |
|---|---|
| Application foundation / WinUI shell | Complete |
| Core system monitoring | Complete |
| Security posture monitoring | Complete |
| Investigation intelligence | Complete core |
| Plain-language user experience | Substantially complete |
| Safe remediation foundation | Complete |
| Remediation decision integration | Complete |
| Recurrence-aware investigation | Complete foundation |
| Autonomous protection core | Complete — 10 of 10 |
| Memory-pressure investigation | Implemented and runtime verified |
| Performance / startup responsiveness | Substantially complete; intermittent lag remains under hardening |
| User approval workflow | Remaining Phase 5 integration |
| Quarantine/recovery management UI | Remaining Phase 5 integration |
| Remediation/investigation history UI | Remaining Phase 5 integration |
| Network endpoint attribution | Remaining Phase 5 integration |
| Background actionable notifications | Remaining Phase 5 integration |
| Failure-path/regression testing | Remaining Phase 5 integration |
| Ask Sentinel / AI Assistance | Planned |
| Installer / update / commercial release readiness | Planned |

---

# Verified Runtime Features

- Native CPU monitoring through `GetSystemTimes`
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- System-drive used, total, and percentage reporting
- Network download and upload throughput
- Running process count and highest-memory process
- Sustained memory-pressure investigation with application contributor context
- Microsoft Defender enabled status
- Windows Firewall enabled status
- Windows Event Log investigation
- Process, service, persistence, and network evidence collection
- Risk and confidence evaluation
- Plain-language investigation summaries
- Healthy-state suppression of unnecessary technical warnings
- Guided Windows actions when user intervention is actually required
- Five-second dashboard refresh cadence
- Deferred first investigation pass so the application shell paints promptly
- Per-Windows-profile preferred-name onboarding

---

# Remediation & Autonomous Protection

Completed foundation and core:

- Central remediation policy and safety gating
- Explicit evidence requirements before system-changing actions
- User approval requirements for moderate/high-risk actions
- Windows protected-component safeguards
- Verified process termination service
- Verified outbound Windows Firewall blocking service
- File quarantine and hash-verified restore service
- Remediation audit trail and outcome state
- Investigation recurrence tracking integrated into the monitoring pipeline
- Remediation recommendation state carried in the system snapshot
- Low-risk automatic-action gating
- Evidence-confidence gating
- Execution-time revalidation
- Safe security-state refresh and transient-operation retry handling
- Verification-pending outcomes rather than unverified success claims
- No automatic force-closing of applications for transient Windows Update error `0x80073D02`

System-changing capabilities remain policy controlled. Sentinel prefers silent monitoring when Windows is expected to self-correct and does not interrupt the user for a single transient condition.

---

# User Experience Direction

Sentinel is designed for non-technical users.

Normal state:

- **Your computer is healthy.**
- Nothing requires attention.
- Technical evidence remains available through progressive disclosure.

Issue state:

- Sentinel investigates first.
- It explains what happened, what is contributing, why it matters, and what should be done.
- It fixes safe conditions automatically only when policy permits.
- It asks the user only when approval, elevation, or a human decision is genuinely required.
- It verifies the outcome after remediation.

---

# Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 8 |
| UI Framework | WinUI 3 |
| Native API Generation | Microsoft.Windows.CsWin32 |
| Target Platform | Windows 10 and later |
| Version Control | Git |
| Production Branch | `main` |
| Build System | MSBuild |

---

# Architecture

```text
User Interface
      │
Investigation / Monitoring Engine
      │
System Snapshot + Evidence
      │
Classification / Guidance / Policy
      │
Remediation + Recurrence Decisions
      │
Verified Windows Remediation Services
      │
Windows and .NET System APIs
```

Core principles:

- Evidence before action
- Least-risk remediation
- Verify every system-changing outcome
- Do not claim success without verification
- Progressive disclosure for technical information
- Keep healthy users undisturbed
- Preserve working features during expansion
- Maintain clear service boundaries

---

# Current Phase 5 Remaining Work

1. Complete user-facing approval workflow for supported moderate-risk actions.
2. Complete quarantine management and safe restore presentation.
3. Add remediation/investigation history presentation without cluttering healthy state.
4. Expand network endpoint attribution and response.
5. Add background/minimized notifications for genuinely actionable findings.
6. Complete failure-path and remediation integration regression testing.
7. Continue startup and investigation performance profiling, including the intermittent lag observed in recent runtime runs.

---

# Later Phases

## Phase 6 — Ask Sentinel / AI Assistance

Natural-language questions grounded in current local evidence, investigation history, and verified system state.

## Phase 7 — Production Hardening & Commercial Release

Remaining release work includes structured diagnostics, performance profiling, long-duration stability testing, Windows compatibility verification, installer/uninstaller, code signing, application updates, privacy documentation, accessibility/UX polish, and release acceptance testing.

---

# Development Workflow

1. Review current implementation and call sites
2. Implement the smallest production-quality increment
3. Commit directly to `main`
4. Pull locally
5. Build
6. Run
7. Verify behavior
8. Update progress/planning documentation at phase boundaries

The application must remain buildable and runnable throughout development.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.
