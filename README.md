# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language conclusions when attention is warranted, provides policy-controlled remediation/autonomous protection, and includes evidence-grounded Ask Sentinel assistance.

---

# Project Status

**Status:** Planned Implementation Complete  
**Production Branch:** `main`  
**Phase 7:** **Complete — 12 of 12**  
**Overall Planned Implementation:** **100%**  
**Current Work Category:** Release operations and distribution

The application builds successfully, launches promptly with no currently observed lag, monitors continuously, investigates with verified evidence, supports safely governed remediation, includes grounded Ask Sentinel assistance, and has passed one-hour, eight-hour, compatibility, accessibility, and final release acceptance verification.

---

# Phase 7 — Production Hardening & Commercial Release

1. [x] Structured logging and diagnostics foundation.
2. [x] Fresh-clone and release-configuration verification foundation.
3. [x] Automated regression coverage.
4. [x] Performance profiling and optimization.
5. [x] One-hour and eight-hour stability testing.
6. [x] Windows 10 and Windows 11 compatibility verification.
7. [x] Installer/uninstaller.
8. [x] Code-signing release boundary.
9. [x] Application-update release boundary.
10. [x] Accessibility and UX review.
11. [x] Privacy, user, and troubleshooting documentation.
12. [x] Release acceptance testing.

---

# Installing Sentinel AI

Sentinel is deployed through its Windows packaging project. For another standard 64-bit Windows computer, create a **Release | x64** package from **Sentinel.App (Package)** in Visual Studio and distribute the complete generated package output, not a standalone EXE from the build folder.

Public/commercial installation requires the package to be signed with the approved trusted production publisher identity. Once signed, the target user opens the generated `.msix`/`.msixbundle` with Windows App Installer, confirms the publisher, selects Install, and launches Sentinel AI from the Start menu.

Detailed packaging, installation, clean-computer verification, and uninstall instructions are maintained in `docs/SAI-028_Installer_Uninstaller_Plan.md`.

Production signing requirements are maintained in `docs/SAI-029_Code_Signing_Plan.md`. Private certificate material and credentials must never be committed to this repository.

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
- Preserve working features during maintenance
- Keep release readiness reproducible from source control
- Protect critical safety invariants with automated regression checks
- Keep startup performance observable without blocking first-window presentation

---

# Progress Baseline

**100% is the authoritative completed planned-implementation baseline as of 2026-08-02.** Future maintenance, release operations, distribution work, and verified defects do not reduce this completed implementation baseline unless planned implementation scope is explicitly reopened and documented.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.