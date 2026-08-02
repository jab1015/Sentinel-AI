# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language conclusions when attention is warranted, provides policy-controlled remediation/autonomous protection, and includes evidence-grounded Ask Sentinel assistance.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Phase 7 — Production Hardening & Commercial Release  
**Phase 7 Progress:** **6 of 12 complete**  
**Current Active Item:** **Installer / Uninstaller implementation and runtime verification**  
**Phase 6:** **Complete — 6 of 6**  
**Estimated Overall Product Progress:** **98%**

The application builds successfully, launches promptly with no currently observed lag, monitors continuously, investigates with verified evidence, supports safely governed remediation, includes grounded Ask Sentinel assistance, runs automated safety regression checks, and has passed both one-hour and eight-hour stability testing.

---

# Phase 7 — Production Hardening & Commercial Release

1. [x] Structured logging and diagnostics foundation.
2. [x] Fresh-clone and release-configuration verification foundation.
3. [x] Automated regression coverage.
4. [x] Performance profiling and optimization.
5. [x] One-hour and eight-hour stability testing.
6. [x] Windows 10 and Windows 11 compatibility verification.
7. [ ] Installer/uninstaller.
8. [ ] Code signing.
9. [ ] Application updates.
10. [ ] Accessibility and UX review.
11. [ ] Privacy, user, and troubleshooting documentation.
12. [ ] Release acceptance testing.

Recent release hardening corrected Windows App SDK startup assumptions exposed during installer development. Personalized greeting persistence and live Technical Details are working again. Sentinel also now prevents historical raw Windows errors and uncorrelated uncommon-port connections from independently producing unsupported Action Required or network-block recommendations.

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
- Never invent system state, history, remediation, threats, actions, or outcomes in AI assistance
- Keep healthy users undisturbed
- Preserve working features during expansion
- Keep release readiness reproducible from source control
- Protect critical safety invariants with automated regression checks
- Keep startup performance observable without blocking first-window presentation

---

# Progress Baseline

**98% is the synchronized overall product baseline as of 2026-08-02.** README, SAI-012 Product Roadmap, SAI-013 Implementation Tracker, and SAI-025 Master Development Plan must remain synchronized. Progress must not move backward unless completed scope is explicitly reopened, removed, or proven incomplete and the reason is documented.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.