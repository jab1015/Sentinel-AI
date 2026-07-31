# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language investigation conclusions only when attention is warranted, and is being expanded with policy-controlled remediation capabilities.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Investigation + Safe Remediation Foundation  
**Phase Progress:** 10 of 10 — implementation foundation complete; runtime verification continues as capabilities are integrated

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

# Safe Remediation Foundation

Implemented foundations:

- Central remediation policy and safety gating
- Explicit evidence requirements before system-changing actions
- User approval requirements for moderate-risk actions
- Windows protected-component safeguards
- Verified process termination service
- Verified outbound Windows Firewall blocking service
- File quarantine and hash-verified restore service
- Persistent remediation audit history
- Persistent investigation history and recurrence counting
- No success claim unless the requested action can be verified
- No automatic force-closing of applications for transient Windows Update error `0x80073D02`

System-changing capabilities remain policy controlled. Sentinel must prefer silent monitoring when Windows is expected to self-correct and must not interrupt the user for a single transient condition.

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
Remediation Services
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
- Remediation audit persistence
- Investigation history persistence
- Startup and refresh responsiveness improvements

---

# Next Phase

Priority order:

1. Integrate remediation services into investigation decisions
2. Add safe automatic remediation classes for low-risk conditions
3. Add user-approval workflow for moderate-risk remediation
4. Add post-remediation verification and UI outcome reporting
5. Use investigation history to distinguish transient from recurring failures
6. Add quarantine management UI and recovery workflow
7. Add remediation/audit history UI only when useful to the user
8. Expand network investigation and endpoint attribution
9. Add notification behavior for issues requiring attention while Sentinel is minimized
10. Complete integration, failure-path, and regression testing

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

Foundation implemented; integration and runtime verification in progress:

- Policy-controlled actions
- Process termination
- Firewall blocking
- Quarantine and restore
- Verification
- Audit/history persistence

## Autonomous Protection

Planned expansion:

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
