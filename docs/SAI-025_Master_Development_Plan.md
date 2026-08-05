# SAI-025 — Master Development Plan

Version: 5.0

Status: Active — Sentinel Discovery 2.0 Complete and Live Validated

Last Updated: 2026-08-05

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains complete for its planned implementation and runtime acceptance. Discovery Acceptance passed 8 of 8 scenarios, Quarantine Acceptance passed 6 of 6 scenarios, and the installed MSIX package passed startup-to-tray validation.

Discovery 2.0 is a post-1.0 product evolution and does not invalidate the accepted 1.0 baseline.

# Product-Wide Sentinel Discovery Rule

Sentinel must not depend on a nontechnical user knowing which technical question to ask.

Every technical condition that Sentinel can safely and reliably verify must participate in continuous Sentinel Discovery. Meaningful findings flow through:

**Discover → Analyze → Correlate → Investigate → Confidence/Trust → Exhaust Safe Remediation → Determine Remaining Risk → Repair/Protect when safe → Request approval when required → Verify result → Record in Activity Center → Preserve Investigation Memory → Feed verified result to Ask Sentinel.**

Ask Sentinel is the explanation and follow-up interface, not the primary discovery mechanism.

# Governing Suppression Rule

**Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.**

A user cannot force Sentinel to ignore a critical, high-risk, actively exploitable, data-loss, malware, active-attack, or mortal hardware-failure condition.

# Sentinel Discovery 2.0 — COMPLETE AND LIVE VALIDATED

## Phase 1 of 5 — Persistent Investigation Intelligence — COMPLETE

Implemented durable persistent investigation memory, stable evidence fingerprints, investigation lifecycle states, repair-attempt history, risk classification, invalidation state, unchanged-evidence reuse, and notification suppression safeguards.

Acceptance: **PASS — 6/6 original persistent-memory scenarios; subsequently expanded policy suite PASS — 10/10.**

## Phase 2 of 5 — Verified Persistent Exceptions — COMPLETE

Implemented presentation policy and suppression eligibility for verified exhausted noncritical findings. Critical and incomplete findings remain visible. Silent monitoring suppresses notifications only and preserves background monitoring. Resume Notifications restores notification behavior without disabling monitoring.

Acceptance: **PASS — persistent investigation/presentation suite 10/10.**

## Phase 3 of 5 — Live Persistent Exception Integration — COMPLETE

Integrated persistent investigation memory with live driver findings and dashboard behavior. Exact matching prevents unrelated findings from inheriting an exception. Healthy state remains quiet. Live suppression preserves monitoring and supports notification restoration.

Acceptance: **PASS — 5/5 live persistent exception scenarios plus live production-path validation.**

## Phase 4 of 5 — Cross-Investigation Correlation — COMPLETE

Implemented evidence correlation across related process/network, service/Event Log, driver/Event Log, and security-control observations. Unsupported relationships remain separate and Sentinel does not invent root cause. Critical evidence retains priority.

Acceptance: **PASS — 7/7 cross-investigation correlation scenarios.**

## Phase 5 of 5 — Trusted Knowledge Engine — COMPLETE

Implemented promotion of completed verified investigations into reusable trusted knowledge with confidence gating, trust requirements, exact evidence compatibility, expiration/revalidation, material-change invalidation, and critical-evidence override.

Acceptance: **PASS — 8/8 trusted knowledge scenarios.**

# Live Integration Validation — PASS

Discovery 2.0 was validated end-to-end against a real persistent Intel(R) Management Engine Interface Code 10 condition.

The first live validation exposed an integration gap: component acceptance passed, but the real driver workflow remained in a repair/manual state and the dashboard continued presenting the condition as requiring attention. Sentinel was corrected so authoritative driver investigation can close the repair ledger when no verified installable repair remains and classify the exact condition as Persistent Noncritical when appropriate.

Final live validation confirmed:

- Sentinel completed the available Windows Update and authoritative manufacturer-source investigation.
- Sentinel found no remaining verified safe installable repair path for the exact Intel(R) Management Engine Interface Code 10 condition.
- The condition was classified as a verified persistent noncritical condition rather than falsely reported as repaired or healthy.
- The dashboard recognized the condition as previously investigated.
- Ask Sentinel reused the persistent investigation conclusion and reported the same result as the Investigation Summary.
- Ask Sentinel surfaced confidence of 92% and authoritative Microsoft/computer-manufacturer trust.
- Monitor Silently became available only after the verified investigation reached the eligible persistent-noncritical state.
- After Monitor Silently was selected, the primary dashboard returned to **Your computer is healthy / Nothing requires your attention right now** while explicitly stating that Sentinel is monitoring a known noncritical condition silently.
- The condition remained in Sentinel's investigation memory and monitoring pipeline.
- Notification suppression did not disable Discovery or background monitoring.
- The design requires material evidence change to invalidate the prior conclusion and reopen investigation.

This live validation is the release-quality proof that the Discovery 2.0 persistent-exception workflow functions through the actual Sentinel UI and investigation path, not only through isolated acceptance harnesses.

# Exhaustive Remediation Rule

Before a noncritical finding can be offered for silent monitoring, Sentinel must evaluate all applicable safe repair paths, such as:

- Windows Update
- Microsoft Update Catalog
- Computer-manufacturer support
- Verified component-manufacturer support
- Driver reinstall or rollback
- Device reset
- Service/dependency repair
- Verified configuration repair
- SFC/DISM when applicable
- BIOS/firmware verification
- Hardware verification

Each path must be recorded as succeeded, failed, unavailable, not applicable, user declined, or awaiting approval.

# Investigation Lifecycle

Every investigation must end in one defined state:

- Resolved
- Requires User Approval
- Requires Manual Repair
- Persistent Noncritical
- Critical
- Investigation Incomplete

A persistent noncritical state is not equivalent to healthy. It means Sentinel has verified the condition, exhausted safe remediation, determined the remaining risk is acceptable for silent monitoring, and will continue watching for change.

# Discovery 2.0 Acceptance Record

Discovery 2.0 acceptance and live validation have verified:

- unchanged investigations can reuse verified memory;
- suppression is rejected for incomplete investigations;
- critical findings cannot be suppressed;
- exhausted noncritical findings can enter silent monitoring;
- monitoring continues while notifications are suppressed;
- exact matching prevents unrelated findings from reusing exceptions;
- material evidence changes invalidate prior conclusions;
- notifications can be resumed without disabling monitoring;
- healthy state remains quiet;
- related evidence is correlated without unsupported root-cause claims;
- unrelated evidence remains independent;
- critical evidence retains investigation priority;
- completed verified investigations can become trusted knowledge;
- low-confidence, incomplete, and critical conclusions are blocked from trusted reuse;
- expired or materially changed knowledge requires fresh investigation/revalidation;
- the real driver-investigation path can reach Persistent Noncritical after verified remediation exhaustion;
- Ask Sentinel and the dashboard present the same persistent-investigation conclusion;
- Monitor Silently returns the dashboard to a quiet healthy presentation while preserving explicit silent-monitoring status.

# Current Progress

- Version 1.0.20.0 baseline: **Complete and accepted**
- Discovery 2.0 planning: **Complete**
- Phase 1 — Persistent Investigation Intelligence: **COMPLETE — PASS**
- Phase 2 — Verified Persistent Exceptions: **COMPLETE — PASS**
- Phase 3 — Live Persistent Exception Integration: **COMPLETE — PASS + LIVE VALIDATION**
- Phase 4 — Cross-Investigation Correlation: **COMPLETE — PASS**
- Phase 5 — Trusted Knowledge Engine: **COMPLETE — PASS**
- Live persistent-condition workflow: **PASS**
- Sentinel Discovery 2.0: **5/5 COMPLETE — END-TO-END LIVE VALIDATED**

---

End of Document
