# SAI-012 — Product Roadmap

**Version:** 4.1  
**Status:** Active — Release Candidate Remediation  
**Last Updated:** 2026-08-04  
**Production Branch:** `main`

## Overall Progress

Core Sentinel AI functionality is implemented, including monitoring, investigation foundations, safe remediation, optimization, maintenance, packaging, stability testing, startup-to-tray operation, and verified containment execution.

Final runtime testing identified four release-candidate areas that remain incomplete or unverified. The product is therefore **not release-ready**.

**Current active phase: Release Candidate Finalization — 0 of 4 runtime-verified.**

**Overall estimated progress: approximately 91%.**

## Completed Major Foundations

- Monitoring and system evidence collection
- Investigation and safe-remediation foundations
- Continuous inbound/outbound connection monitoring
- Spyware/process correlation
- Process containment acceptance
- Firewall block/removal acceptance
- Quarantine/restore backend acceptance
- Optimization and maintenance foundations
- One-hour and eight-hour stability testing
- Installer/uninstaller foundation
- Clean install/uninstall testing
- Windows startup-to-tray verification
- Network recovery and sleep/wake verification

## Release Candidate Finalization

### 1 of 4 — Ask Sentinel Local

**Status: Incomplete**

Finish local evidence providers and runtime verification for Windows Update, pending restart, TPM, Secure Boot, BitLocker/device encryption, Defender, Firewall, uptime, CPU, memory, disk, startup applications, services, networking, and top processes.

Ask Sentinel must remain grounded in verified local evidence and must not perform live web searches.

### 2 of 4 — Quarantine Manager UI

**Status: Incomplete**

Add a visible user interface for the existing quarantine backend, including item history, reason/evidence summary, restore confirmation, permanent removal, verification state, and Activity Center linkage.

### 3 of 4 — Activity Center

**Status: Incomplete**

Add a visible 30-day activity history showing automatic repairs, optimizations, investigations, quarantine/restore events, rollbacks, verification outcomes, and user-required actions. Sentinel must tell the user when it successfully fixes something.

### 4 of 4 — Investigation Engine Runtime Integration

**Status: Incomplete / Unverified**

Demonstrate end-to-end runtime operation:

- Local evidence collection
- Confidence scoring
- Internal authoritative web research only when local evidence is insufficient
- Correlation of research with local evidence
- Safe automatic repair or continued monitoring
- Repair verification
- Activity Center logging
- Stored findings available to Ask Sentinel

The web-research capability is only for Sentinel AI to resolve problems automatically. It is not a general Ask Sentinel web-search feature.

## Final Acceptance

Final Acceptance Test 8 remains open. It will be rerun only after all four Release Candidate Finalization items are working in the installed product.

## Release Gate

Do not approve a production release, describe the product as complete, or report 100% progress until:

1. All four Release Candidate Finalization items pass runtime validation.
2. Final Acceptance Test 8 passes.
3. Planning/progress documents are updated to completed status.
4. The final production package is built, signed, and verified.

## Product Rule

Sentinel must investigate before acting, keep healthy users undisturbed, verify system-changing outcomes, remain transparent about uncertainty, clearly report successful fixes, and use authoritative web research only as an internal problem-resolution tool when local evidence is insufficient.
