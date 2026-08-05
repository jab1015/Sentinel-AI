# SAI-012 — Product Roadmap

**Version:** 4.8  
**Status:** Active — Discovery 2.0, Adaptive Continuous Discovery, and Event-Driven Discovery Complete  
**Last Updated:** 2026-08-05  
**Production Branch:** `main`

## Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains **100% complete for its planned implementation and runtime acceptance**.

The accepted baseline includes proactive Discovery, verified investigation, safe remediation policy, Ask Sentinel, Activity Center, Quarantine Manager, continuous tray operation, packaging, and Windows startup-to-tray behavior.

## Completed Product Initiative — Discovery 2.0

Discovery 2.0 extends Sentinel from proactive monitoring into a persistent, memory-based investigation platform.

- Phase 1 — Persistent Investigation Intelligence: **COMPLETE**
- Phase 2 — Verified Persistent Exceptions: **COMPLETE**
- Phase 3 — Live Persistent Exception Integration: **COMPLETE + LIVE VALIDATED**
- Phase 4 — Cross-Investigation Correlation: **COMPLETE**
- Phase 5 — Trusted Knowledge Engine: **COMPLETE**

**Discovery 2.0: 5/5 COMPLETE and end-to-end live validated.**

## Completed Product Initiative — Adaptive Continuous Discovery

Adaptive Continuous Discovery makes Sentinel's monitoring effort responsive to risk, user attention state, power conditions, and system activity while preserving continuous monitoring.

- Phase 1 — Adaptive Cadence Policy: **COMPLETE — 7/7 PASS**
- Phase 2 — Live Monitoring Loop Integration: **COMPLETE — BUILD PASS**
- Phase 3 — Live Adaptive Scheduling Acceptance: **COMPLETE — 7/7 PASS**
- Phase 4 — Adaptive Diagnostics and Final Acceptance: **COMPLETE — 6/6 PASS**

**Adaptive Continuous Discovery: 4/4 COMPLETE.**

## Completed Product Initiative — Event-Driven Discovery

Event-Driven Discovery adds material-change responsiveness on top of adaptive polling. Sentinel no longer has to wait for the next ordinary interval when current evidence changes in a way that requires immediate re-evaluation.

### Phase 1 — Material Change Detection — COMPLETE

- Critical evidence appearance detected.
- Security-posture changes detected.
- Evidence-fingerprint changes detected.
- Material changes to silently monitored persistent conditions detected.
- Attention transitions detected.
- Power/idle operating-context changes detected.
- Unchanged evidence remains quiet.
- Urgent changes are separated from cadence-only recalculation.
- Acceptance: **8/8 PASS.**

### Phase 2 — Live State Coordinator — COMPLETE

- Previous live state retained for comparison.
- Fingerprint, Defender/Firewall, critical, attention, suppression, power, and idle state compared across observations.
- Specific security-event classification preserved.
- Initial state and unchanged snapshots do not create false events.
- Acceptance: **8/8 PASS.**

### Phase 3 — Live Runtime Integration — COMPLETE

- Live snapshots feed Event-Driven Discovery evaluation.
- Urgent material changes can request immediate confirmation refresh.
- Unchanged conditions stay on Adaptive Continuous Discovery cadence.
- Confirmation snapshots settle without recursive refresh loops.
- Materially changed silently monitored conditions reopen automatically.
- Security-posture transitions interrupt ordinary cadence.
- Nonurgent power/idle changes and attention clearing recalculate without false urgency.
- Build: **PASS.** Runtime acceptance: **8/8 PASS.**

### Phase 4 — Event Diagnostics and Final Acceptance — COMPLETE

- Unchanged evidence produces no diagnostic noise.
- Fingerprint changes explain immediate recheck behavior.
- Duplicate identical events are suppressed.
- Security posture changes receive specific event labeling.
- Reopened persistent conditions explain why investigation resumed.
- Operating-context changes remain explicitly nonurgent.
- Diagnostics always preserve monitoring-enabled state.
- Acceptance: **7/7 PASS.**

**Event-Driven Discovery: 4/4 COMPLETE.**

## Governing Product Rules

**Persistent-condition rule:** Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.

**Adaptive-monitoring rule:** Sentinel may vary monitoring cadence according to verified risk and operating conditions, but adaptive scheduling must never disable monitoring. Critical evidence always overrides reduced cadence and silent persistent-condition presentation.

**Event-driven rule:** A material evidence change may interrupt ordinary adaptive cadence and force immediate re-evaluation. Unchanged evidence must remain quiet, nonurgent context changes must not be falsely escalated, and confirmation refreshes must settle without recursive loops.

## Current Roadmap State

- Version 1.0.20.0 baseline — **COMPLETE / ACCEPTED**
- Discovery 2.0 — **5/5 COMPLETE / LIVE VALIDATED**
- Adaptive Continuous Discovery — **4/4 COMPLETE**
- Event-Driven Discovery — **4/4 COMPLETE**

## Parallel Release Work

Public distribution signing remains a separate installer milestone. The self-signed Modern Methods certificate is suitable for controlled testing but does not eliminate certificate trust prompts on unrelated customer computers. Public-trust signing or Store distribution must be completed before broad customer release.

---

End of Document
