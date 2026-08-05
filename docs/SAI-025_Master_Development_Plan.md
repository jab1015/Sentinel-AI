# SAI-025 — Master Development Plan

Version: 5.2

Status: Active — Discovery 2.0, Adaptive Continuous Discovery, and Event-Driven Discovery Complete

Last Updated: 2026-08-05

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains complete for its planned implementation and runtime acceptance. Discovery Acceptance passed 8 of 8 scenarios, Quarantine Acceptance passed 6 of 6 scenarios, and the installed MSIX package passed startup-to-tray validation.

Discovery 2.0, Adaptive Continuous Discovery, and Event-Driven Discovery are post-1.0 product evolutions and do not invalidate the accepted 1.0 baseline.

# Product-Wide Sentinel Discovery Rule

Sentinel must not depend on a nontechnical user knowing which technical question to ask.

Every technical condition that Sentinel can safely and reliably verify must participate in continuous Sentinel Discovery. Meaningful findings flow through:

**Discover → Analyze → Correlate → Investigate → Confidence/Trust → Exhaust Safe Remediation → Determine Remaining Risk → Repair/Protect when safe → Request approval when required → Verify result → Record in Activity Center → Preserve Investigation Memory → Feed verified result to Ask Sentinel.**

Ask Sentinel is the explanation and follow-up interface, not the primary discovery mechanism.

# Governing Suppression Rule

**Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.**

A user cannot force Sentinel to ignore a critical, high-risk, actively exploitable, data-loss, malware, active-attack, or mortal hardware-failure condition.

# Sentinel Discovery 2.0 — 5/5 COMPLETE AND LIVE VALIDATED

Persistent Investigation Intelligence, Verified Persistent Exceptions, Live Persistent Exception Integration, Cross-Investigation Correlation, and Trusted Knowledge Engine are complete. Acceptance suites passed 6/6 persistent-memory scenarios, 10/10 expanded presentation-policy scenarios, 5/5 live persistent-exception scenarios, 7/7 correlation scenarios, and 8/8 Trusted Knowledge scenarios.

Discovery 2.0 was validated end-to-end against a real persistent Intel(R) Management Engine Interface Code 10 condition. Sentinel completed authoritative investigation, determined no remaining verified safe repair path existed, classified the condition as Persistent Noncritical, aligned Ask Sentinel with the Investigation Summary, offered Monitor Silently only after eligibility was established, and returned the dashboard to a quiet healthy presentation while preserving background monitoring and investigation memory.

# Adaptive Continuous Discovery — 4/4 COMPLETE

Adaptive Continuous Discovery makes Sentinel's continuous monitoring cadence responsive to current risk and system conditions rather than relying on one fixed polling interval.

- Phase 1 — Adaptive Cadence Policy: **COMPLETE — 7/7 PASS**
- Phase 2 — Live Monitoring Loop Integration: **COMPLETE — BUILD PASS**
- Phase 3 — Live Adaptive Scheduling Acceptance: **COMPLETE — 7/7 PASS**
- Phase 4 — Adaptive Diagnostics and Final Acceptance: **COMPLETE — 6/6 PASS**

Adaptive scheduling may change how frequently Sentinel rechecks evidence, but it never disables monitoring. Critical evidence always receives urgent priority, including when a prior noncritical condition is being monitored silently.

# Event-Driven Discovery — 4/4 COMPLETE

Event-Driven Discovery extends adaptive scheduling so material evidence changes can interrupt ordinary polling cadence and trigger immediate re-evaluation when warranted.

## Phase 1 of 4 — Material Change Detection — COMPLETE

Implemented evidence-oriented material-change classification for critical evidence appearance, security-posture changes, evidence-fingerprint changes, persistent-condition material changes, attention transitions, and operating-context changes.

Unchanged evidence does not trigger event-driven rechecks. Security, critical, fingerprint, and materially changed silently monitored persistent conditions can force immediate re-evaluation. Attention clearing and operating-context changes remain material for scheduling without false urgency.

Acceptance: **PASS — 8/8 scenarios.**

## Phase 2 of 4 — Live State Coordinator — COMPLETE

Implemented live state memory and comparison across Discovery snapshots. The coordinator tracks previous evidence fingerprint, Defender/Firewall posture, critical state, attention state, persistent suppression state, power source, and idle context.

Specific security-posture classification takes precedence when security state changes, preventing a generic critical classification from hiding the more useful security event type.

Acceptance: **PASS — 8/8 live coordinator scenarios.**

## Phase 3 of 4 — Live Runtime Integration — COMPLETE

Integrated live snapshots with event-driven evaluation so material changes can request an immediate confirmation refresh instead of waiting for the next adaptive interval. Confirmation snapshots settle without recursive refresh loops. Unchanged evidence remains on normal adaptive cadence.

Silently monitored persistent conditions reopen when their evidence materially changes. Security posture changes interrupt ordinary cadence. Power/idle changes and attention clearing recalculate scheduling without unnecessary urgent refreshes.

Build: **PASS.** Runtime acceptance: **PASS — 8/8 scenarios.**

## Phase 4 of 4 — Event Diagnostics and Final Acceptance — COMPLETE

Implemented low-noise Event-Driven Discovery diagnostics. Unchanged evidence produces no diagnostic noise. Material fingerprint changes explain immediate rechecks. Duplicate identical events are suppressed. Security posture changes receive specific labeling. Reopened silent persistent conditions explain the reopening path. Operating-context changes remain explicitly nonurgent. Diagnostics preserve the fact that monitoring remains enabled.

Acceptance: **PASS — 7/7 scenarios.**

# Event-Driven Discovery Operating Policy

- Ordinary unchanged evidence remains governed by Adaptive Continuous Discovery cadence.
- Material evidence change invalidates the unchanged-state assumption.
- New critical evidence and security-posture changes receive immediate re-evaluation.
- Evidence-fingerprint changes force immediate confirmation.
- A materially changed silently monitored persistent condition automatically reopens for investigation.
- Attention appearance may force immediate re-evaluation; attention clearing recalculates scheduling without false urgency.
- Power and idle context changes may alter cadence without forcing an urgent investigation.
- Confirmation refreshes must settle without recursive refresh loops.
- Event diagnostics must be low-noise and must never imply monitoring has stopped.

# Current Progress

- Version 1.0.20.0 baseline: **COMPLETE AND ACCEPTED**
- Sentinel Discovery 2.0: **5/5 COMPLETE — END-TO-END LIVE VALIDATED**
- Adaptive Continuous Discovery: **4/4 COMPLETE**
- Event-Driven Discovery Phase 1 — Material Change Detection: **COMPLETE — 8/8 PASS**
- Event-Driven Discovery Phase 2 — Live State Coordinator: **COMPLETE — 8/8 PASS**
- Event-Driven Discovery Phase 3 — Live Runtime Integration: **COMPLETE — BUILD PASS + 8/8 PASS**
- Event-Driven Discovery Phase 4 — Diagnostics/Final Acceptance: **COMPLETE — 7/7 PASS**
- Event-Driven Discovery: **4/4 COMPLETE**

---

End of Document
