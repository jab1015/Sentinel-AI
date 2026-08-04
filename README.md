# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, presents plain-language conclusions when attention is warranted, provides policy-controlled remediation/autonomous protection, and includes evidence-grounded Ask Sentinel assistance.

---

# Project Status

**Status:** Release Candidate Remediation  
**Production Branch:** `main`  
**Overall Estimated Progress:** **approximately 91%**  
**Release Ready:** **No**

Core monitoring, protection, containment, optimization, maintenance, packaging, startup-to-tray, stability, clean install/uninstall, network recovery, and sleep/wake foundations are implemented and verified.

Final runtime testing identified four incomplete or unverified release-candidate areas:

1. Ask Sentinel Local evidence coverage
2. Quarantine Manager UI
3. Activity Center UI and repair visibility
4. Investigation Engine runtime integration and verification

Final Acceptance Test 8 remains open.

---

# Release Candidate Finalization

## 1 of 4 — Ask Sentinel Local

The UI works, but local evidence coverage is incomplete. Remaining providers include Windows Update, pending restart, TPM, Secure Boot, BitLocker/device encryption, and broader Windows health questions.

Ask Sentinel remains local-only and does not perform live web searches.

## 2 of 4 — Quarantine Manager UI

The quarantine/restore backend and acceptance harness pass, but the installed product still needs a visible Quarantine Manager with item history, evidence summary, restore, permanent removal, verification state, and activity linkage.

## 3 of 4 — Activity Center

The product still needs a visible 30-day history for automatic repairs, optimizations, investigations, quarantine/restore actions, rollbacks, verification results, and user-required actions. Sentinel must clearly tell the user when it successfully fixes something.

## 4 of 4 — Investigation Engine Runtime Integration

The internal investigation workflow must be demonstrated end-to-end: local evidence collection, confidence scoring, authoritative web research when local evidence is insufficient, safe repair decisions, verification, Activity Center logging, and stored findings available to Ask Sentinel.

The web-research capability exists only to help Sentinel resolve problems automatically. It is not a general Ask Sentinel internet-search feature.

---

# Verified Foundations

- Continuous system and security monitoring
- Inbound/outbound connection monitoring
- Spyware/process correlation
- Process containment acceptance
- Firewall block/removal acceptance
- Quarantine/restore backend acceptance
- Optimization and maintenance foundations
- One-hour and eight-hour stability testing
- Clean install and uninstall
- Windows startup-to-tray
- Network disconnect/recovery
- Sleep/wake recovery

---

# Installing Sentinel AI

Sentinel is deployed through its Windows packaging project. For another standard 64-bit Windows computer, create a **Release | x64** package from **Sentinel.App (Package)** in Visual Studio and distribute the complete generated package output, not a standalone EXE from the build folder.

Public/commercial installation requires the package to be signed with the approved trusted production publisher identity. Private certificate material and credentials must never be committed to this repository.

Detailed packaging, installation, clean-computer verification, and uninstall instructions are maintained in `docs/SAI-028_Installer_Uninstaller_Plan.md`.

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
- Never invent system state, history, remediation, threats, actions, or outcomes
- Keep healthy users undisturbed
- Tell users when Sentinel successfully fixes something
- Keep Ask Sentinel grounded in verified local evidence
- Use authoritative web research only for internal problem resolution when local evidence is insufficient

---

# Release Gate

Sentinel AI must not be described as complete, commercially ready, or 100% finished until all four Release Candidate Finalization items pass runtime validation and Final Acceptance Test 8 passes.

---

# Author

**Modern Methods**  
Product Owner

Copyright (c) 2026 Modern Methods.
