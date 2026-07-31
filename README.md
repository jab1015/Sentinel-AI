# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language conclusions when attention is warranted, provides policy-controlled remediation/autonomous protection, and includes evidence-grounded Ask Sentinel assistance.

---

# Project Status

**Status:** Active Development  
**Production Branch:** `main`  
**Current Phase:** Phase 7 — Production Hardening & Commercial Release  
**Phase 7 Progress:** **2 of 12 complete**  
**Phase 6:** **Complete — 6 of 6**  
**Estimated Overall Product Progress:** **98%**

The application builds, launches promptly, monitors continuously, investigates with verified evidence, supports safely governed remediation, and includes grounded Ask Sentinel assistance with history awareness, recommendation safeguards, and final fail-safe response validation.

---

# Phase 7 — Production Hardening & Commercial Release

1. [x] Structured logging and diagnostics foundation.
2. [x] Fresh-clone and release-configuration verification foundation.
3. [ ] Automated regression coverage.
4. [ ] Performance profiling and optimization.
5. [ ] One-hour and eight-hour stability testing.
6. [ ] Windows 10 and Windows 11 compatibility verification.
7. [ ] Installer/uninstaller.
8. [ ] Code signing.
9. [ ] Application updates.
10. [ ] Accessibility and UX review.
11. [ ] Privacy, user, and troubleshooting documentation.
12. [ ] Release acceptance testing.

`tools/Verify-ReleaseConfiguration.ps1` now provides a repeatable source-controlled release configuration check for required project/package files, target framework and Windows version alignment, x86/x64/ARM64 support, package references, package entry-point wiring, and required CsWin32 declarations.

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

---

# Progress Baseline

**98% is the synchronized overall product baseline as of 2026-07-31.** README, SAI-012 Product Roadmap, SAI-013 Implementation Tracker, and SAI-025 Master Development Plan must remain synchronized. Progress must not move backward unless completed scope is explicitly reopened, removed, or proven incomplete and the reason is documented.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.