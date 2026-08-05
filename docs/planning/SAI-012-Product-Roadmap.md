# SAI-012 — Product Roadmap

**Version:** 4.7  
**Status:** Active — Discovery 2.0 and Adaptive Continuous Discovery Complete  
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

Acceptance includes persistent investigation 6/6, expanded policy 10/10, live persistent exception 5/5, correlation 7/7, Trusted Knowledge 8/8, and end-to-end live Intel(R) Management Engine Interface Code 10 validation.

**Discovery 2.0: 5/5 COMPLETE and end-to-end live validated.**

## Completed Product Initiative — Adaptive Continuous Discovery

Adaptive Continuous Discovery makes Sentinel's monitoring effort responsive to risk, user attention state, power conditions, and system activity while preserving continuous monitoring.

### Phase 1 — Adaptive Cadence Policy — COMPLETE

- Critical / High / Medium / Low Discovery priorities.
- Risk-sensitive recheck intervals.
- Battery-aware noncritical throttling.
- Idle-system deeper-verification allowance.
- Monitoring remains enabled under every scheduling decision.
- Acceptance: **7/7 PASS.**

### Phase 2 — Live Monitoring Loop Integration — COMPLETE

- Adaptive policy connected to the live monitoring/dashboard loop.
- Critical conditions can tighten to approximately 2-second cadence.
- Active-attention conditions use approximately 5-second cadence.
- Quiet systems back off from legacy fixed polling.
- Silently monitored persistent noncritical conditions remain monitored without forcing high-frequency polling.
- Critical/security evidence overrides silent-monitoring cadence.
- Build: **PASS.**

### Phase 3 — Live Adaptive Scheduling Acceptance — COMPLETE

- Quiet systems verified at 30-second cadence.
- Active attention verified at 5 seconds.
- Critical evidence verified at 2 seconds.
- Silent persistent conditions verified as low-priority while monitoring remains enabled.
- Critical override verified.
- Unnecessary unchanged timer resets prevented.
- Quiet battery systems verified at one-minute cadence.
- Acceptance: **7/7 PASS.**

### Phase 4 — Adaptive Diagnostics and Final Acceptance — COMPLETE

- Low-noise cadence diagnostics implemented.
- Meaningful cadence transitions are recorded and explained.
- Duplicate unchanged-cadence events are suppressed.
- Attention and critical acceleration are explained.
- Silent persistent monitoring explicitly states monitoring continues.
- Diagnostics never report monitoring as disabled when adaptive scheduling is active.
- Acceptance: **6/6 PASS.**

**Adaptive Continuous Discovery: 4/4 COMPLETE.**

## Governing Product Rules

**Persistent-condition rule:** Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.

**Adaptive-monitoring rule:** Sentinel may vary monitoring cadence according to verified risk and operating conditions, but adaptive scheduling must never disable monitoring. Critical evidence always overrides reduced cadence and silent persistent-condition presentation.

## Current Roadmap State

- Version 1.0.20.0 baseline — **COMPLETE / ACCEPTED**
- Discovery 2.0 — **5/5 COMPLETE / LIVE VALIDATED**
- Adaptive Continuous Discovery — **4/4 COMPLETE**

## Parallel Release Work

Public distribution signing remains a separate installer milestone. The self-signed Modern Methods certificate is suitable for controlled testing but does not eliminate certificate trust prompts on unrelated customer computers. Public-trust signing or Store distribution must be completed before broad customer release.

---

End of Document
