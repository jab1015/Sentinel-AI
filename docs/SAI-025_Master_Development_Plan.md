# SAI-025 — Master Development Plan

Version: 5.1

Status: Active — Discovery 2.0 and Adaptive Continuous Discovery Complete

Last Updated: 2026-08-05

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains complete for its planned implementation and runtime acceptance. Discovery Acceptance passed 8 of 8 scenarios, Quarantine Acceptance passed 6 of 6 scenarios, and the installed MSIX package passed startup-to-tray validation.

Discovery 2.0 and Adaptive Continuous Discovery are post-1.0 product evolutions and do not invalidate the accepted 1.0 baseline.

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

# Discovery 2.0 Live Integration Validation — PASS

Discovery 2.0 was validated end-to-end against a real persistent Intel(R) Management Engine Interface Code 10 condition. Sentinel completed authoritative investigation, determined no remaining verified safe repair path existed, classified the condition as Persistent Noncritical, aligned Ask Sentinel with the Investigation Summary, offered Monitor Silently only after eligibility was established, and returned the dashboard to a quiet healthy presentation while preserving background monitoring and investigation memory.

# Adaptive Continuous Discovery — 4/4 COMPLETE

Adaptive Continuous Discovery makes Sentinel's continuous monitoring cadence responsive to current risk and system conditions rather than relying on one fixed polling interval.

## Phase 1 of 4 — Adaptive Cadence Policy — COMPLETE

Implemented Critical, High, Medium, and Low Discovery priorities; urgent recheck intervals; battery-aware throttling; idle-system deep-verification allowance; and a governing rule that adaptive scheduling never disables monitoring.

Acceptance: **PASS — 7/7 scenarios.**

Verified critical conditions receive immediate cadence, high-priority findings recheck quickly, medium findings use normal cadence, battery mode reduces noncritical polling, idle systems can permit deeper verification, quiet battery systems use lowest-impact cadence, and escalation overrides ordinary attention priority.

## Phase 2 of 4 — Live Monitoring Loop Integration — COMPLETE

Integrated adaptive cadence decisions into the live dashboard/Discovery scheduling path. Critical conditions can tighten refreshes to approximately 2 seconds, active attention conditions use approximately 5 seconds, quiet systems back off, and silently monitored persistent findings do not force high-frequency polling by themselves.

Critical and security evidence continues to override suppression and receives urgent scheduling.

## Phase 3 of 4 — Live Adaptive Scheduling Acceptance — COMPLETE

Implemented and executed live scheduler acceptance coverage.

Acceptance: **PASS — 7/7 scenarios.**

Verified quiet-system backoff to 30 seconds, active-attention 5-second cadence, critical 2-second cadence, low-impact scheduling for silently monitored persistent findings, critical override of persistent suppression, avoidance of unnecessary timer resets, and one-minute quiet-system cadence on battery while monitoring remains enabled.

## Phase 4 of 4 — Adaptive Diagnostics and Final Acceptance — COMPLETE

Implemented low-noise adaptive cadence diagnostics so meaningful scheduling transitions can be explained without flooding Activity history. Diagnostics record why Sentinel speeds up or slows down, preserve explicit monitoring-enabled state, explain silent persistent monitoring, and suppress duplicate unchanged-cadence events.

Acceptance: **PASS — 6/6 scenarios.**

Verified initial cadence event recording, duplicate-event suppression, 5-second attention transition explanation, 2-second critical transition explanation, continued-monitoring explanation for silent persistent conditions, and diagnostics that never imply monitoring has been disabled.

# Adaptive Continuous Discovery Operating Policy

Sentinel must adapt monitoring effort to risk while preserving continuous protection:

- Critical verified evidence receives the fastest supported recheck cadence.
- Active attention findings receive elevated cadence.
- Moderate evidence receives normal cadence.
- Quiet systems back off to reduce unnecessary work.
- Battery operation reduces noncritical polling impact.
- Idle systems may permit deeper background verification.
- Silently monitored persistent noncritical conditions remain monitored without forcing high-frequency notification-oriented polling.
- Critical evidence always overrides silent-monitoring cadence.
- An adaptive cadence decision must never disable monitoring.
- Diagnostic history should record meaningful cadence transitions, not repetitive unchanged state.

# Current Progress

- Version 1.0.20.0 baseline: **COMPLETE AND ACCEPTED**
- Sentinel Discovery 2.0: **5/5 COMPLETE — END-TO-END LIVE VALIDATED**
- Adaptive Continuous Discovery Phase 1 — Cadence Policy: **COMPLETE — 7/7 PASS**
- Adaptive Continuous Discovery Phase 2 — Live Loop Integration: **COMPLETE — BUILD PASS**
- Adaptive Continuous Discovery Phase 3 — Live Scheduling Acceptance: **COMPLETE — 7/7 PASS**
- Adaptive Continuous Discovery Phase 4 — Diagnostics/Final Acceptance: **COMPLETE — 6/6 PASS**
- Adaptive Continuous Discovery: **4/4 COMPLETE**

---

End of Document
