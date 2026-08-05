# SAI-012 — Product Roadmap

**Version:** 4.6  
**Status:** Active — Sentinel Discovery 2.0 Complete and Live Validated  
**Last Updated:** 2026-08-05  
**Production Branch:** `main`

## Version 1.0 Baseline

Sentinel AI version 1.0.20.0 remains **100% complete for its planned implementation and runtime acceptance**.

The accepted baseline includes proactive Discovery, verified investigation, safe remediation policy, Ask Sentinel, Activity Center, Quarantine Manager, continuous tray operation, packaging, and Windows startup-to-tray behavior.

## Completed Product Initiative — Discovery 2.0

Discovery 2.0 extends Sentinel from proactive monitoring into a persistent, memory-based investigation platform.

### Phase 1 — Persistent Investigation Intelligence — COMPLETE

- Persist completed investigation records.
- Create stable, evidence-based fingerprints.
- Reuse unchanged verified conclusions.
- Avoid repeating expensive investigations when no material evidence has changed.
- Track investigation lifecycle states and invalidation conditions.
- Acceptance passed.

### Phase 2 — Verified Persistent Exceptions — COMPLETE

- Require verified completion and exhausted remediation before silent monitoring.
- Add noncritical/persistent risk classification.
- Offer notification suppression only when no safe verified repair remains.
- Prevent suppression of critical and incomplete findings.
- Continue monitoring while notifications are suppressed.
- Automatically invalidate prior conclusions on material evidence change.
- Acceptance passed.

### Phase 3 — Live Persistent Exception Integration — COMPLETE + LIVE VALIDATED

- Match live findings to persistent investigation memory.
- Apply exact-fingerprint presentation policy.
- Keep healthy states quiet.
- Hide eligible repeated notifications without stopping monitoring.
- Restore notifications independently of monitoring.
- Prevent unrelated findings from inheriting an exception.
- Ensure the real driver-investigation lifecycle reaches Persistent Noncritical only after verified remediation exhaustion.
- Feed the persistent conclusion into Ask Sentinel so the conversational answer and dashboard agree.
- Acceptance harness passed 5/5.
- End-to-end live workflow passed against Intel(R) Management Engine Interface Code 10.

### Phase 4 — Cross-Investigation Correlation — COMPLETE

- Group related observations into one investigation when evidence supports the relationship.
- Correlate process/network, service/Event Log, driver/Event Log, and security-control evidence.
- Preserve critical priority.
- Keep unrelated evidence independent.
- Avoid unsupported root-cause claims.
- Acceptance passed 7/7.

### Phase 5 — Trusted Knowledge Engine — COMPLETE

- Convert completed verified investigations into reusable trusted knowledge.
- Store evidence state, outcomes, confidence, trust, risk, repair history, and expiration rules.
- Reject incomplete, critical, or low-confidence promotion.
- Revalidate after material evidence change or expiration.
- Never allow trusted knowledge to bypass current critical evidence.
- Acceptance passed 8/8.

## Governing Product Rule

**Sentinel must never suppress a finding until it has completed a verified investigation, exhausted every applicable safe remediation, determined that the condition is noncritical, and verified that there is currently nothing more it can safely do. Suppression hides notifications only; it never stops monitoring. Any material change automatically reopens the investigation.**

## Discovery 2.0 User Experience — LIVE VALIDATED

For an eligible persistent noncritical condition, Sentinel explains the verified investigation state and can suppress repeated notifications while continuing background monitoring. Notifications can be restored without disabling monitoring, and material evidence change invalidates the prior conclusion.

The live driver workflow has now demonstrated the intended user experience:

1. Sentinel discovers the condition.
2. Sentinel investigates Windows Update and authoritative manufacturer sources.
3. When no verified safe repair remains, Sentinel classifies the exact condition as persistent noncritical.
4. The dashboard identifies it as a previously investigated condition.
5. Ask Sentinel explains the same verified conclusion with confidence/trust information.
6. Monitor Silently becomes available.
7. After selection, the dashboard returns to a quiet healthy state while clearly stating that a known noncritical condition is being monitored silently.
8. Monitoring and investigation memory remain active.
9. Material evidence change is designed to reopen the investigation automatically.

The user must never be offered a blanket **ignore all similar issues** option.

## Discovery 2.0 Acceptance Milestones

- Persistent investigation storage and exact fingerprinting — **PASS**
- Exhaustive-remediation/suppression policy — **PASS**
- Silent-monitoring presentation policy — **PASS**
- Live persistent exception harness — **PASS 5/5**
- Live persistent-condition end-to-end workflow — **PASS**
- Cross-investigation correlation — **PASS 7/7**
- Trusted Knowledge Engine — **PASS 8/8**

**Discovery 2.0: 5/5 phases complete and end-to-end live validated.**

## Parallel Release Work

Public distribution signing remains a separate installer milestone. The self-signed Modern Methods certificate is suitable for controlled testing but does not eliminate certificate trust prompts on unrelated customer computers. Public-trust signing or Store distribution must be completed before broad customer release.

---

End of Document
