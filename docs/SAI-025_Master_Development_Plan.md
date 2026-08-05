# SAI-025 — Master Development Plan

Version: 4.6

Status: Active — Final Production Validation

Last Updated: 2026-08-04

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Product-Wide Sentinel Discovery Rule

Sentinel must not depend on a nontechnical user knowing which technical question to ask.

Every technical condition that Sentinel can safely and reliably verify must participate in continuous Sentinel Discovery. Meaningful findings must flow through the common lifecycle:

**Discover → Analyze → Investigate → Confidence/Trust → Determine Action → Repair/Protect when safe → Request approval when required → Verify result → Roll back when applicable → Record in Activity Center → Feed verified result to Ask Sentinel.**

Ask Sentinel is the explanation and follow-up interface. It is not the primary discovery mechanism.

Supported technical areas must be incorporated into this rule wherever Sentinel has sufficient verified evidence, including drivers/devices, Windows Update and restart state, services, processes, startup items, scheduled tasks, CPU/memory/disk health, Defender/firewall, network connections and suspicious activity, persistence/spyware indicators, Windows security configuration, system/event-log failures, quarantine state, and other implemented Windows health/security evidence.

Sentinel must never invent a diagnosis or silently perform an action whose safety has not been verified. When an automatic repair cannot be proven safe, Sentinel must still make the finding actionable by explaining it simply, performing authoritative investigation when appropriate, and presenting the correct user-approved next step.

# Current Status

Sentinel Discovery Expansion is complete and product-wide acceptance passed all eight scenarios.

Verified product-wide behavior now includes proactive discovery, actionable investigation, safe automatic classification, approval-gated remediation, guided user actions, observation-only handling for insufficient evidence, plain-language primary UX, investigation-history persistence, Activity Center integration, Ask Sentinel reuse, and quiet behavior for healthy evidence.

The product remains at **99%** until final production validation and release-document synchronization are complete.

# Current Progress

- Planning and architecture: **Complete**
- Core platform and monitoring: **Complete**
- Protection and containment foundation: **Complete**
- Optimization and maintenance foundation: **Complete**
- Stability and packaging foundation: **Complete**
- Release Candidate Finalization foundations: **Complete**
- Investigation Engine runtime integration: **Complete**
- Sentinel Discovery Expansion: **4 of 4 complete**
- Product-wide Discovery Acceptance: **PASS — 8 of 8 scenarios**
- Overall estimated progress: **99%**

# Sentinel Discovery Expansion

## 1 of 4 — Driver Reference Workflow

**Status: COMPLETE — runtime verified**

The proactive driver workflow is the reference pattern for product-wide Sentinel Discovery.

## 2 of 4 — Technical Monitor Discovery Integration

**Status: COMPLETE**

Implemented proactive Discovery integration for supported evidence across drivers/devices, Windows Update and restart state, Defender/firewall, processes, services, startup and scheduled-task evidence, memory and disk conditions, network activity, spyware correlation, event-log findings, Secure Boot, TPM, and other implemented Windows health/security evidence.

Normal evidence remains quiet unless a verified condition requires user attention.

## 3 of 4 — Actionability and Safe Remediation

**Status: COMPLETE**

Supported findings are classified as:

1. Safe automatic action.
2. Approval-required action.
3. Guided user action.
4. Observation only when evidence does not justify a system change.

Actions remain subject to verification, remediation policy, approval requirements, rollback where applicable, Activity Center logging, and Ask Sentinel grounding.

## 4 of 4 — Product-Wide Runtime Acceptance

**Status: COMPLETE — PASS**

The Discovery Acceptance Harness passed all eight scenarios:

1. Healthy evidence remains quiet.
2. Defender disabled is proactive and safely actionable.
3. Correlated network behavior requires approval.
4. Uncorroborated process evidence remains observation-only.
5. Driver findings are guided and approval-gated.
6. Windows Update is guided and not silently installed.
7. Secure Boot remains a guided firmware action.
8. Critical disk pressure is guided to Windows Storage.

**Result: PASS — 8/8.**

# UX Requirement

Primary user-facing messages must be concise and nontechnical. Detailed evidence such as device counts, unsigned-driver counts, event counts, hardware IDs, source-selection logic, and other diagnostic data belongs in Technical details unless it is necessary for the user's decision.

During initial startup Discovery, Sentinel must clearly tell the user that it is gathering and analyzing current system evidence before showing a health conclusion.

# Final Production Validation — NEXT

Final release validation must confirm the integrated installed product rather than isolated components.

Required verification:

1. Clean launch and visible initial Discovery state.
2. Proactive actionable finding appears without Ask Sentinel prompting when a verified condition exists.
3. Ask Sentinel answers from current verified evidence and investigation history.
4. Quarantine Manager opens and remains functional.
5. Activity Center persists verified outcomes.
6. Investigation Engine and confidence/trust behavior remain intact.
7. Approval-required actions remain gated.
8. No false healthy state when an actionable verified condition exists.
9. Healthy evidence remains quiet.
10. No crashes, debug breaks, clipping, freezes, or material startup/runtime lag.

After the final production validation passes:

- synchronize all planning/progress/release documents;
- produce the final release package;
- mark Sentinel AI 100% only after the release gate is satisfied.

# Release Gate

Sentinel AI must not be described as product-complete, commercially ready, or 100% finished until final production validation passes in the integrated application and release documentation is synchronized.

---

End of Document
