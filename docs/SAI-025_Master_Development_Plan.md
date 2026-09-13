# SAI-025 — Master Development Plan

Version: 6.0  
Status: Active — Production security hardening  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Purpose

This is the authoritative engineering plan for moving Sentinel AI from its existing monitoring/intelligence foundation to a trustworthy active Windows security product.

## Current Reality

Historical `1.0.25.0 Release Candidate Validated` evidence remains useful as a prior runtime baseline, but it is superseded for release decisions by the September 2026 production assessment at commit `1218f5d...`.

Assessment: **29 findings — 14 High, 15 Medium**.  
Hardening branch: `security/production-hardening-1218f5d`.  
Pre-documentation checkpoint: `d8ac4af17a6451cb4f82e86ce45b64523f1ed303`.  
Release posture: **DO NOT MERGE / NOT PRODUCTION-HARDENED**.

## Product-Wide Evidence Rule

Sentinel must describe only what evidence proves. Unknown/unavailable is not healthy. Detection is not blocking. Unsigned is not automatically malicious. An attempted action is not a successful action. AI text is advisory unless deterministic product state establishes the claim.

## Product-Wide Action Rule

Every destructive or privileged action must be:

**Discover → Correlate → Establish confidence → Identify exact target → Obtain required approval → Revalidate identity/state → Execute through a narrow boundary → Verify postcondition → Preserve recovery evidence → Record outcome → Explain accurately.**

## Workstream 1 — Bounded Execution

Status: **SOURCE/CI SUBSTANTIALLY VALIDATED**

- Concurrent output draining.
- Timeout/cancellation.
- Output limits.
- Process-tree termination.
- Production PowerShell argument handling.
- 10 consecutive Windows CI acceptance iterations PASS.

Remaining: access-denied/kill-failure and packaged runtime cases where practical.

## Workstream 2 — Protected Quarantine

Status: **SOURCE/CI SUBSTANTIALLY HARDENED; RUNTIME ADVERSARIAL WORK OPEN**

Implemented/tested:

- Protected store + records + transactions.
- Semantic recovery guard.
- Ambiguous/forged recovery fails closed.
- Handle-based restore/delete identity controls.
- Hash and hard-link protections.
- No-overwrite restore behavior.
- Crash-recovery scenarios.
- Metadata cleanup failure returns non-success and preserves transaction evidence.

Remaining: standard-user ACL/owner proof, reparse/junction/symlink/hardlink attacks, races, all crash checkpoints, disk-full/access-denied, orphan states, broker termination, installed UAC/package behavior.

## Workstream 3 — Privileged Broker

Status: **ACTIVE**

Implemented/source-tested:

- Versioned allowlisted protocol.
- Current-user named pipe.
- Peer PID binding.
- Exact process target start/path/hash checks.
- Package-full-name identity binding between broker and client.
- Policy harness rejects mismatched/missing/unpackaged identities.
- Generated MSIX contains broker and desktop executable.

Next: hostile IPC and installed elevated/UAC/package lifecycle matrix.

## Workstream 4 — Ask Sentinel Claim Safety

Status: **NEXT CODE PRIORITY**

Known gap: MainWindow can replace an already validated orchestrator response for optimization, external-investigation, and driver-answer paths. Add one final deterministic validator/provenance boundary immediately before display after every replacement/composition path, then add regression tests for unsupported security-action claims.

## Workstream 5 — Authenticode

Status: **SOURCE/CI IMPROVED; EXTENDED RUNTIME MATRIX OPEN**

Validate embedded/catalog, unsigned/tampered, self-signed/untrusted, timestamps, revocation/offline behavior, replacement races/cache invalidation, and every shipped architecture.

## Workstream 6 — AI Gateway / Entitlements

Status: **SOURCE IMPLEMENTED; GOOGLE CLOUD + STORE VALIDATION BLOCKED**

Source includes signed short-lived sessions, tier enforcement, Store entitlement path, replay request IDs, provider concurrency limits, and server-side provider credentials. Next is deployed staging abuse, IAM/Secret Manager, distributed state/spend, outage/restart, and live Store entitlement validation.

## Workstream 7 — Remaining Findings

Continue A06, A08, A10, A11, A12, A13, A14, A16, A18, A20-A29 with smallest-correct-change + deterministic-test discipline. Unsafe capabilities may remain explicitly disabled/fail-closed rather than restored prematurely.

## Workstream 8 — Final Assurance

After source remediation:

1. Re-audit every original High finding adversarially.
2. Re-audit all 29 findings.
3. Build final signed/Store-style package.
4. Clean install/upgrade/uninstall.
5. Validate supported Windows and shipped architectures.
6. Standard/admin/UAC/startup/background/Defender/firewall/recovery matrices.
7. Fresh final-commit 1-hour and 8-hour stability/resource runs.
8. Independent final production/security review.

## Current CI Checkpoint

At `d8ac4af1...`:

- Windows workflow PASS: `34671410981` (same-head run `34671409289` also PASS).
- Package workflow PASS: `34671410865`.

These are internal gates only; no finding should be labeled fully PASS unless all required evidence for that finding exists.

---

End of Document
