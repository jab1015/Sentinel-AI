# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language investigation conclusions only when attention is warranted, and provides policy-controlled remediation foundations.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Remediation Integration Complete — Autonomous Protection Next  
**Remediation Integration Progress:** 10 of 10 — complete

The application builds, launches, monitors the system continuously, suppresses non-actionable findings, and has been repeatedly runtime-verified by the Product Owner.

---

# Verified Runtime Features

- Native CPU monitoring through `GetSystemTimes`
- Native physical-memory monitoring through `GlobalMemoryStatusEx`
- System-drive used, total, and percentage reporting
- Network download and upload throughput
- Running process count and highest-memory process
- Microsoft Defender enabled status
- Windows Firewall enabled status
- Windows Event Log investigation
- Process, service, persistence, and network evidence collection
- Risk and confidence evaluation
- Plain-language investigation summaries
- Healthy-state suppression of unnecessary technical warnings
- Guided Windows actions when user intervention is actually required
- Five-second investigation refresh cadence to reduce UI lag
- Deferred first investigation pass so the application shell paints promptly

---

# Remediation Integration

Completed integration foundation:

- Central remediation policy and safety gating
- Explicit evidence requirements before system-changing actions
- User approval requirements for moderate-risk actions
- Windows protected-component safeguards
- Verified process termination service
- Verified outbound Windows Firewall blocking service
- File quarantine and hash-verified restore service
- Remediation audit trail and outcome state
- Investigation recurrence tracking integrated into the monitoring pipeline
- Recurrence and escalation state carried in the system snapshot
- Remediation recommendation state carried in the system snapshot
- No success claim unless the requested action can be verified
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
- It explains what happened and why it matters.
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

# Completed Development Areas

- Core system monitoring
- Security posture monitoring
- Event Log investigation
- Process investigation
- Service investigation
- Persistence/startup investigation
- Active network evidence
- Investigation guidance and confidence
- Healthy-state suppression
- Transient Windows Update suppression
- Remediation policy foundation
- Process remediation foundation
- Firewall remediation foundation
- Quarantine/restore foundation
- Remediation audit trail
- Investigation recurrence tracking
- Remediation and recurrence snapshot integration
- Startup and refresh responsiveness improvements

---

# Next Phase — Autonomous Protection

Priority order:

1. Execute approved low-risk remediation automatically when policy permits
2. Connect remediation outcomes to the user-facing investigation summary
3. Add explicit approval workflow for actions that require user consent
4. Perform post-remediation verification before declaring an issue resolved
5. Use recurrence escalation to increase intervention only when a verified condition persists
6. Add quarantine management and recovery workflow
7. Add remediation/audit history UI only when useful to the user
8. Expand network investigation and endpoint attribution
9. Add notification behavior for issues requiring attention while Sentinel is minimized
10. Complete integration, failure-path, performance, and regression testing

---

# Roadmap

## Investigation Intelligence

Substantially implemented:

- System evidence collection
- Windows Event intelligence
- Process/service/persistence/network evidence
- Risk and confidence scoring
- Plain-language conclusions
- Progressive disclosure

## Safe Remediation

Integration foundation complete:

- Policy-controlled actions
- Process termination
- Firewall blocking
- Quarantine and restore
- Verification foundations
- Audit state
- Recurrence-aware escalation state

## Autonomous Protection

Next active phase:

- Low-risk automatic correction
- Recurrence-aware escalation
- Verified recovery actions
- Minimal user interruption
- Continuous protection while minimized

## Commercial Release

- Production installer
- Automatic updates
- Complete automated and runtime test coverage
- Performance hardening
- Accessibility and UX polish
- Release documentation
- Public-release readiness

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
