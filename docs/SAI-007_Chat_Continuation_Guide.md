# SAI-007 — Chat Continuation Guide

Version: 2.1  
Status: Active — Production hardening handoff  
Last Updated: 2026-09-12

---

## Purpose

Use this document to continue Sentinel AI hardening without restarting or repeating completed work.

## Repository State

- Repository: `jab1015/Sentinel-AI`
- Baseline assessment commit: `1218f5d39e2e98f955179d7911b013636068d373`
- Working branch: `security/production-hardening-1218f5d`
- Last fully proven broad checkpoint: `3ef08da9226e33a222768938b3dff13373ba7f61`
- Latest production-source hardening checkpoint: `7507e52b7619fcd45a15929d234745ab9f57ec82`
- Live branch head before the current documentation refresh was `f0524d2e07bafc7d3f963a505b740027ef48224c`.
- Commits after `7507e52b...` through that observed head are documentation-only. Do not treat them as a newer source implementation checkpoint.
- Authoritative finding tracker: `docs/SENTINEL_AI_PRODUCTION_REMEDIATION.md`
- Authoritative roadmap: `docs/SAI-005_Product_Roadmap.md`
- Do not merge to `main` during hardening.

## Current CI Evidence

Last fully proven broad checkpoint `3ef08da9...`:

- Windows hardening `34677065591`: SUCCESS.
- Unsigned x64 package `34677065599`: SUCCESS.
- Driver repair `34677065596`: SUCCESS.
- Optimization state `34677065594`: SUCCESS.

Additional focused evidence:

- External research provenance `34678939880`: SUCCESS on `92554ed...`.
- Child process safety `34679015786`: SUCCESS on `03048af...`.
- Investigation cache `34679063934`: SUCCESS on `019ddedf...`.
- Earlier broad Windows run `34697575639`: SUCCESS on `de08012e...`. This is useful later-source evidence but is not exact-source-checkpoint evidence for `7507e52b...`.

At the latest observation, the twelve workflow runs generated for source checkpoint `7507e52b...` remain QUEUED:

- Investigation Cache `34699409592`
- Child Process Safety `34699409606`
- Architecture `34699409613`
- A14 Temporary Cleanup `34699409614`
- Subprocess Boundary Audit `34699409627`
- Security Hardening Package `34699409643`
- Security Hardening Windows `34699409676`
- Driver Repair `34699409689`
- Package Architecture `34699409679`
- External Research `34699409742`
- Network Throughput `34699409843`
- Optimization State `34699409786`

Queued/running checks are not PASS. Older superseded hardening runs are draining ahead of this wave. Do not create source churn simply to reset the queue.

## Current Work Order

1. Keep production source frozen unless exact-source CI exposes a concrete defect.
2. For a failure, inspect the exact failing job/log, classify root cause, reproduce with the smallest relevant harness, correct production or harness/workflow only where evidence proves it is wrong, preserve/assert security semantics, add regression coverage, then rerun targeted and broad gates.
3. Promote A09/A17 only after exact-source Subprocess Boundary Audit and broad Windows gates are green.
4. Promote A21/A22 only after exact-source External Research, Child Process Safety, Investigation Cache, and applicable broad Windows/package gates are green.
5. Promote A24 only after exact-source Network Throughput and broad Windows gates are green.
6. Preserve A14 and A16 as SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED; complete their installed destructive-operation/recovery matrices once automated qualification is clean.
7. Validate A29 package PE architecture without dropping x86 or ARM64 or weakening the verifier; cross-build/package success is not runtime qualification.
8. When source/automated gates are clean, begin installed Windows testing: A07/A15 broker/UAC adversarial matrix, A01 Authenticode fixtures, quarantine, A14 cleanup, A16 service restart, A19 final-claim paths, driver/device behavior, network churn, startup/lifecycle, Defender/firewall, crash/failure matrices.
9. Execute A05 Google Cloud + Store entitlement staging.
10. Complete signed Store package/install-update-uninstall and x86/x64/ARM64 runtime qualification, fresh final-commit 1-hour/8-hour stability, High re-audit, all-29 re-audit, and new-vulnerability review.

## Quarantine Handoff

Deterministic CI covers basic quarantine/restore/delete, collision, tamper, crash recovery, corrupt/forged transactions, forged restore/temp paths, duplicate IDs, and forced metadata cleanup failure. Restore/delete do not report success when protected metadata cleanup fails; transaction evidence is preserved.

Still open at runtime: standard-user ACL resistance, resulting ACL/owner verification, reparse/junction/symlink/hardlink attacks, source/destination replacement races, orphan states, all crash checkpoints, disk full/access denied, destination directory deletion, broker killed mid-operation, and installed package/UAC behavior.

## Premium Privacy Preservation

Phase F in `SAI-005_Product_Roadmap.md` remains PLANNED and must not be implemented during the current hardening/release program unless explicitly authorized.

Preserve broad attributable-copy discovery for future Secure Delete. Discovery should search as broadly as Windows and configured storage services safely permit for reasonably discoverable copies, versions, history entries, references, and Sentinel-created artifacts attributable to the selected file. Candidates are classified as CONFIRMED COPY, LIKELY ATTRIBUTABLE COPY, METADATA / REFERENCE ONLY, or UNVERIFIED CANDIDATE. Fuzzy filename similarity alone never authorizes deletion. Discovery and deletion remain separate, with explicit reporting of searched sources, found/removed/remaining items, inaccessible sources, and facts Sentinel cannot prove.

Preserve the encryption roadmap: AES-256-GCM, fresh per-file data keys, unique nonce material, authenticated/versioned metadata, safe verify-before-plaintext-removal transactions, Windows-account key wrapping, portable password mode with reviewed KDF (Argon2id preferred where supportable), independent recovery keys, Sentinel Vault master-key/per-item-key wrapping, and future Windows Hello/TPM only after separate review.

## Development Rules

- Read live branch + tracker + current CI before changing code.
- Reproduce/isolate root cause first.
- Make the smallest correct security change.
- Add deterministic regression tests.
- Run targeted evidence, then full Windows/package gates.
- Never weaken tests, hide failures, convert failures to warnings, or merely raise timeouts to make CI green.
- Preserve ambiguous evidence; fail closed on destructive uncertainty.
- Refresh branch immediately before writes; if it advanced, review concurrent changes and never force-overwrite.
- Use status language precisely: OPEN, SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED, BLOCKED — external/runtime validation, or PASS only when fully verified.

## Release Rule

Maximum internal conclusion before independent review is `READY FOR FINAL INDEPENDENT REVIEW`. Do not claim production-ready until all required runtime/external/release evidence is complete.

---

End of Document
