# SAI-012 — Product Roadmap

**Version:** 4.4  
**Status:** Active — Sentinel Discovery 2.0  
**Last Updated:** 2026-08-05  
**Production Branch:** `main`

## Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains **100% complete for its planned implementation and runtime acceptance**.

The accepted baseline includes proactive Discovery, verified investigation, safe remediation policy, Ask Sentinel, Activity Center, Quarantine Manager, continuous tray operation, packaging, and Windows startup-to-tray behavior.

## Current Product Initiative — Discovery 2.0

Discovery 2.0 extends Sentinel from proactive monitoring into a persistent, memory-based investigation platform.

### Phase 1 — Persistent Investigation Intelligence

- Persist completed investigation records.
- Create stable, evidence-based fingerprints.
- Reuse unchanged verified conclusions.
- Avoid repeating expensive investigations when no material evidence has changed.
- Track investigation lifecycle states and invalidation conditions.

### Phase 2 — Verified Persistent Exceptions

- Add exhaustive remediation proof.
- Add noncritical/persistent risk classification.
- Offer **Monitor silently** only after Sentinel proves no safe verified repair remains.
- Prevent suppression of critical, high-risk, active-attack, malware, data-loss, or mortal hardware-failure conditions.
- Continue monitoring while notifications are suppressed.
- Automatically reactivate on material change.

### Phase 3 — Cross-Investigation Correlation

- Group related observations into one root-cause investigation.
- Assign confidence to relationships.
- Keep low-confidence links internal.
- Surface conflicting evidence without guessing.
- Reduce duplicate warnings and technical noise.

### Phase 4 — Adaptive Continuous Discovery

- Move toward event-driven discovery.
- Prioritize critical/high/medium/low investigation work.
- Reduce background load while gaming, rendering, or on battery.
- Run deeper work when idle.
- Reopen investigations only when meaningful evidence changes.

### Phase 5 — Trusted Knowledge Engine

- Convert completed investigations into reusable verified knowledge.
- Store evidence, outcomes, confidence, trust, risk, repair history, and expiration rules.
- Revalidate knowledge after Windows, driver, BIOS/firmware, device, severity, or authoritative-source changes.
- Never reuse a conclusion when its invalidation conditions have been met.

## Governing Product Rule

**Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.**

## Discovery 2.0 User Experience

For an eligible persistent noncritical condition, Sentinel should explain:

- what it found;
- what it investigated;
- which repair paths were exhausted;
- why no safe verified repair remains;
- why the condition is not critical;
- what will reactivate the notification.

Available user actions:

- Keep reminding me
- Monitor silently
- Resume reminders

The user must never be offered a blanket **ignore all similar issues** option.

## Acceptance Milestones

- Persistent investigation storage and exact fingerprinting
- Exhaustive remediation ledger
- Risk/suppression eligibility policy
- Silent-monitoring UI
- Automatic reactivation
- Activity Center integration
- Ask Sentinel integration
- Correlation engine
- Adaptive event-driven discovery
- Trusted Knowledge Engine
- Expanded Discovery 2.0 acceptance harness

## Parallel Release Work

Public distribution signing remains a separate installer milestone. The self-signed Modern Methods certificate is suitable for controlled testing but does not eliminate certificate trust prompts on unrelated customer computers. Public-trust signing or Store distribution must be completed before broad customer release.

---

End of Document
