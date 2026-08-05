# SAI-025 — Master Development Plan

Version: 4.8

Status: Active — Sentinel Discovery 2.0

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

# Sentinel Discovery 2.0

## Phase 1 of 5 — Persistent Investigation Intelligence

Sentinel must stop treating every Discovery cycle as a new investigation.

Each verified investigation receives a persistent record containing:

- Investigation ID
- Finding type and root cause
- Evidence and confidence/trust
- Repair attempts and results
- Risk classification
- Current lifecycle state
- Stable finding fingerprint
- Last verification date
- Conditions that invalidate the conclusion

The fingerprint may include device instance ID, hardware ID, error code, driver version, Windows build, BIOS version, manufacturer/model, and investigation type.

When the same unchanged fingerprint appears again, Sentinel reuses the verified conclusion instead of repeating expensive investigation work.

## Phase 2 of 5 — Verified Persistent Exceptions

A finding is eligible for silent monitoring only when Sentinel proves:

1. The investigation is complete.
2. Every applicable safe, authoritative remediation has been attempted, ruled out, declined, or proven unavailable.
3. No safe verified automatic repair remains.
4. The condition is noncritical and not a mortal failure.
5. The exception applies only to the exact verified fingerprint.

Eligible user choices:

- Keep reminding me
- Monitor silently
- Resume reminders

Silent monitoring must automatically end when any material evidence changes, including error code, device identity, hardware ID, driver version, Windows build, BIOS/firmware version, severity, repair availability, device replacement, or newly verified authoritative guidance.

## Phase 3 of 5 — Cross-Investigation Correlation

Sentinel must correlate related findings into one root-cause investigation rather than presenting duplicate technical warnings.

Correlation sources include drivers/devices, services, event logs, processes, process lineage, command lines, startup entries, scheduled tasks, Defender, Firewall, network activity, storage, Windows Update, TPM, Secure Boot, BIOS/firmware, and other supported evidence.

Low-confidence relationships remain internal. Conflicting evidence must be reported as unresolved rather than guessed.

## Phase 4 of 5 — Adaptive Continuous Discovery

Discovery should become increasingly event-driven and context-aware.

Sentinel should prioritize meaningful system changes, reduce unnecessary repeated scans, defer low-priority work while the computer is busy or on battery, and perform deeper checks when idle.

Priority classes:

- Critical — immediate investigation
- High — investigate within seconds
- Medium — queue for investigation
- Low — investigate when idle

## Phase 5 of 5 — Trusted Knowledge Engine

Completed investigations become reusable verified knowledge records.

Each knowledge record stores evidence, outcome, confidence, trust, risk, repair history, last verification, and explicit invalidation conditions.

Knowledge must be revalidated when material system or authoritative-source evidence changes. Sentinel may reuse a conclusion only when the current fingerprint remains compatible with the stored record.

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

# Discovery 2.0 Acceptance Requirements

Before Discovery 2.0 is complete, acceptance must prove:

- unchanged investigations are reused rather than repeated;
- suppression is impossible before exhaustive remediation and noncritical classification;
- critical findings cannot be suppressed;
- silent monitoring preserves evidence collection;
- exact-fingerprint matching prevents overbroad suppression;
- material changes reactivate the finding automatically;
- suppression and reactivation are written to Activity Center;
- Ask Sentinel accurately explains the investigation state;
- correlated findings appear as one root-cause investigation where justified;
- healthy and unchanged persistent findings remain quiet;
- all system-changing actions remain verified and approval-gated where required.

# Current Progress

- Version 1.0.20.0 baseline: **Complete and accepted**
- Discovery 2.0 planning: **Complete**
- Phase 1 — Persistent Investigation Intelligence: **Starting implementation**
- Phase 2 — Verified Persistent Exceptions: **Planned**
- Phase 3 — Cross-Investigation Correlation: **Planned**
- Phase 4 — Adaptive Continuous Discovery: **Planned**
- Phase 5 — Trusted Knowledge Engine: **Planned**

---

End of Document
