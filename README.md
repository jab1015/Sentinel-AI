# Sentinel AI

> **Windows Investigation, Security & Remediation Assistant**

Sentinel AI is a Windows desktop application built with **WinUI 3** and **.NET 8**. It continuously evaluates system and security evidence, provides evidence-grounded assistance, performs safe verified actions, and explains its work in plain language.

---

# Project Status

**Status:** Feature Complete — Final Packaged Validation Pending
**Production Branch:** `main`
**Current Version:** **1.0.25.0**
**Release Readiness:** **99% — refreshed MSIX and final regression pending**

The current Release build has passed the final evidence-accuracy, subscription-boundary, Activity Center, optimization-transparency, and BSOD-response live checks. A refreshed installed package must still pass Store-entitlement, packaged-startup, complete automated regression, and resource smoke validation before release.

# System Evidence Accuracy

The user-facing System Evidence panel is evidence-bound and names what Sentinel actually measures:

- **CPU Usage** — current processor utilization from Windows system-time counters.
- **Physical Memory** — physical RAM used/total and percentage.
- **Windows System Drive** — used/total capacity for the Windows system drive, not all disks.
- **Current Network Activity** — live receive/send throughput; it is not an internet bandwidth or Speedtest-style capability measurement.
- **Running Processes** — current process count and highest working-memory process.
- **Windows Security Evidence** — free basic Defender/Firewall status. Advanced Sentinel correlation, proactive security, external/cloud investigation, optimization, repair, containment, and quarantine require verified subscription entitlement.
- **Evidence Collected** — timestamp of the evidence snapshot being displayed.

If Sentinel cannot verify a value, it must say so. It must not invent values or label one measurement as a different capability.

# Optimization Transparency

Sentinel establishes a free local performance baseline. Applying automatic optimization changes requires verified subscription entitlement, explicit user opt-in, and the mandatory evidence, verification, and rollback safety policy.

Recent Activity and Optimization Status are separate:

- **Recent Activity** preserves recorded Sentinel actions and investigations.
- **Optimization Status** reports the current optimization assessment even when no change is needed.
- Actual Sentinel maintenance actions must be persistently recorded and must outrank passive status checks in attribution.
- Sentinel never claims credit for Windows maintenance that it cannot attribute to Sentinel.
- Completed optimization is reported only when the action and result are verified.

# Completed Intelligence Systems

## Sentinel Discovery 2.0

Complete and live validated.

- Persistent Investigation Intelligence
- Investigation memory
- Trusted knowledge workflow
- Cross-investigation correlation
- Verified persistent exceptions

## Adaptive Continuous Discovery

Complete. Sentinel adjusts monitoring behavior based on system conditions while maintaining continuous protection.

## Event-Driven Discovery

Complete. Sentinel responds to meaningful evidence changes immediately instead of relying only on scheduled checks.

## Friendly AI Value Layer

Complete. Sentinel explains verified work in user-friendly language, including verified maintenance, cleanup, Windows health work, network repairs, and other confirmed outcomes. Sentinel only reports actions that were completed and verified.

# Verified Foundations

- Continuous system and security monitoring
- Incoming and outgoing network telemetry
- Investigation Engine
- Persistent investigation memory
- Ask Sentinel grounded responses
- Safe remediation foundations
- Maintenance and optimization reporting
- Activity Center value reporting
- System Evidence accuracy and semantics audit
- Optimization transparency and attribution
- Windows startup-to-tray operation
- Stability validation
- MSIX production packaging

# Product Rules

- Evidence before action
- Verify outcomes before claiming success
- Never invent system state or results
- Name measurements according to what they actually represent
- Never claim attribution for maintenance Sentinel cannot prove it performed
- Keep healthy users undisturbed while still confirming ongoing monitoring/optimization status
- Explain important work clearly
- Preserve investigation history
- Keep Ask Sentinel grounded in verified evidence

# Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| Framework | .NET 8 |
| UI | WinUI 3 |
| Native API | Microsoft.Windows.CsWin32 |
| Platform | Windows 10 and later |

---

# Author

**Modern Methods**

Copyright (c) 2026 Modern Methods.
