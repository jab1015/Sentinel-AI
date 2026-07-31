# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language conclusions when attention is warranted, provides policy-controlled remediation/autonomous protection, and is now adding evidence-grounded Ask Sentinel assistance.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Phase 6 — Ask Sentinel / AI Assistance  
**Phase 6 Progress:** **1 of 6 complete**  
**Phase 5:** **Complete**  
**Autonomous Protection Core:** **Complete — 10 of 10**  
**Estimated Overall Product Progress:** **88%**

The application builds, launches promptly, monitors continuously, suppresses non-actionable findings, investigates causes using local evidence, supports safely governed remediation, and has been repeatedly runtime verified by the Product Owner.

---

# Overall Progress

| Area | Status |
|---|---|
| Application foundation / WinUI shell | Complete |
| Core system monitoring | Complete |
| Security posture monitoring | Complete |
| Investigation Engine | Complete — 18 of 18 |
| Investigation experience | Complete core |
| Safe Remediation Foundation | Complete — 10 of 10 |
| Autonomous Protection core | Complete — 10 of 10 |
| Phase 5 remediation integration | Complete — 7 of 7 |
| Preferred-name onboarding | Complete / runtime verified |
| Memory-pressure investigation | Complete / runtime verified |
| Startup responsiveness | Current behavior accepted; further hardening in Phase 7 |
| Ask Sentinel / AI Assistance | Active — 1 of 6 |
| Production hardening / commercial release | Phase 7 planned / partially underway |

---

# User Experience Direction

Sentinel is designed for non-technical users. When the system is healthy, it stays calm and says so. When evidence warrants attention, Sentinel investigates first, explains what happened and why it matters, identifies contributing causes when evidence supports them, recommends or performs safe actions under policy, requests approval when required, and verifies system-changing outcomes.

Technical details remain available through progressive disclosure rather than overwhelming the normal experience.

---

# Phase 5 — Complete

Phase 5 completed remediation integration and Autonomous Protection, including:

- User-facing approval workflow and execution-time approval revalidation
- Quarantine/restore foundations and presentation integration
- Investigation/remediation history presentation
- Network attribution/response integration
- Background actionable attention signaling
- Failure-path and remediation regression fail-safe safeguards
- Accepted current startup/load behavior
- Autonomous Protection core — 10 of 10
- Remaining Phase 5 integration milestone — 7 of 7

---

# Phase 6 — Ask Sentinel / AI Assistance

**Status: Active — 1 of 6 complete**

1. [x] Grounded local evidence context layer.
2. [ ] Natural-language Ask Sentinel interaction surface.
3. [ ] Evidence-grounded response orchestration.
4. [ ] Investigation-history-aware explanations.
5. [ ] Explainable recommendations with strict no-invention safeguards.
6. [ ] Integration, failure-path, and runtime verification.

Ask Sentinel must remain grounded in verified Sentinel evidence. It must explicitly acknowledge insufficient evidence rather than inventing system state, threats, causes, history, or remediation outcomes.

---

# Phase 7 — Production Hardening & Commercial Release

Remaining release work includes structured diagnostics, fresh-clone/release verification, automated regression coverage, performance optimization, long-duration stability tests, Windows 10/11 verification, installer/uninstaller, code signing, application updates, privacy/user/troubleshooting documentation, accessibility/UX polish, and release acceptance testing.

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

# Core Product Rules

- Evidence before action
- Investigate causes rather than merely report metrics
- Least-risk remediation
- Verify system-changing outcomes
- Never claim success without verification
- Never invent system state in AI assistance
- Keep healthy users undisturbed
- Preserve working features during expansion

---

# Progress Baseline

**88% is the synchronized overall product baseline as of 2026-07-31.** README, SAI-012 Product Roadmap, SAI-013 Implementation Tracker, and SAI-025 Master Development Plan must remain synchronized. Progress must not move backward unless completed scope is explicitly reopened, removed, or proven incomplete and the reason is documented.

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