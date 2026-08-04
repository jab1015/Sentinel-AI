# SAI-025 — Master Development Plan

Version: 4.5

Status: Active — Sentinel Discovery Expansion

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

The Release Candidate Finalization foundations are implemented and runtime verification has confirmed Ask Sentinel history integration, Quarantine Manager, Activity Center persistence, Investigation Engine history reuse, and proactive driver discovery.

The proactive driver workflow is the reference implementation for the product-wide Sentinel Discovery rule. Runtime evidence confirms that Sentinel can discover a Windows-reported driver/device problem without the user asking about drivers, change the dashboard from healthy to attention-required, show an Investigation Summary, and expose the verified driver-repair workflow.

The product remains at **99%** while Sentinel Discovery is expanded across the remaining supported technical monitors and the final acceptance suite is rerun.

# Current Progress

- Planning and architecture: **Complete**
- Core platform and monitoring: **Complete**
- Protection and containment foundation: **Complete**
- Optimization and maintenance foundation: **Complete**
- Stability and packaging foundation: **Complete**
- Release Candidate Finalization foundations: **Complete**
- Investigation Engine runtime integration: **Functionally verified**
- Sentinel Discovery Expansion: **1 of 4 complete**
- Overall estimated progress: **99%**

# Sentinel Discovery Expansion

## 1 of 4 — Driver Reference Workflow

**Status: COMPLETE — runtime verified**

Verified behavior:

- Driver/device health is checked proactively.
- A Windows-reported device/driver problem changes the dashboard from healthy to attention-required.
- The user does not have to ask Ask Sentinel to discover the condition.
- The dashboard provides a clear Review driver repair action.
- Investigation Summary explains what was found, why it matters, what was investigated, and what happens next.
- Repair investigation uses Windows Update first and authoritative manufacturer research when required.
- Automatic installation is not permitted unless the exact repair is verified safe and installable.
- Installation and restart remain approval-gated.
- Investigation outcomes are persisted to Recent Activity/history.
- Ask Sentinel can reuse the verified historical finding later.
- Historical answers reconcile the prior finding against current system evidence.

The driver workflow is now the reference pattern for all remaining Discovery categories.

## 2 of 4 — Technical Monitor Discovery Integration

**Status: NEXT**

Inventory every implemented technical monitor and evidence collector. Identify any area that currently collects evidence but does not proactively create an actionable finding when a meaningful verified condition exists.

Connect supported findings to the common Discovery/Investigation/Action pipeline. Priority areas include:

- Windows Update and pending restart
- Defender and firewall
- suspicious processes and persistence indicators
- startup applications and services
- scheduled tasks where supported
- CPU, memory, and disk health conditions
- incoming/outgoing connections and suspicious network activity
- system and application event-log failures
- security configuration evidence
- quarantine and containment state

Normal/healthy evidence should remain quiet and should not overwhelm the user.

## 3 of 4 — Actionability and Safe Remediation

**Status: PENDING**

For every supported Discovery category, classify the verified finding into one of these outcomes:

1. Safe automatic protection/maintenance action is available.
2. A repair can be prepared but requires user approval.
3. Authoritative investigation is required before an action can be selected.
4. User action is required because Sentinel cannot safely perform the repair.
5. Observation only; no action is justified.

Any executed action must be verified. Reversible actions must support rollback where technically applicable. Results must be written to Activity Center and made available to Ask Sentinel.

## 4 of 4 — Product-Wide Runtime Acceptance

**Status: PENDING**

Runtime-test representative conditions from each supported Discovery category and confirm:

- proactive detection without a user question
- no false healthy state when a verified actionable condition exists
- plain-English primary UX
- technical evidence available through progressive disclosure
- correct confidence/trust handling
- safe action selection
- approval gates
- repair/protection verification
- rollback where applicable
- Activity Center persistence
- Ask Sentinel reuse of verified findings
- quiet behavior for normal conditions

After this passes, rerun Final Acceptance Test 8 and synchronize all release documentation.

# UX Requirement

Primary user-facing messages must be concise and nontechnical. Detailed evidence such as device counts, unsigned-driver counts, event counts, hardware IDs, source-selection logic, and other diagnostic data belongs in Technical details unless it is necessary for the user's decision.

Sentinel should tell the user what matters:

- what it found
- whether the user needs to do anything
- what Sentinel can safely do
- what approval is required
- whether the problem was fixed

# Final Acceptance

After Sentinel Discovery Expansion 4 of 4 passes:

1. Re-run Final Acceptance Test 8.
2. Confirm no clipping, crashes, debug breaks, freezes, or material lag.
3. Confirm proactive Discovery, Ask Sentinel, Quarantine Manager, Activity Center, and Investigation Engine work together in the installed product.
4. Confirm normal conditions remain quiet while verified actionable conditions are surfaced proactively.
5. Update all planning/progress documents to completed status.
6. Produce the final signed release package and release notes.

# Release Gate

Sentinel AI must not be described as product-complete, commercially ready, or 100% finished until the product-wide Sentinel Discovery rule is implemented across supported technical areas, Sentinel Discovery Expansion 4 of 4 passes, and Final Acceptance Test 8 passes.

---

End of Document
