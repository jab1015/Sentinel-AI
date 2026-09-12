# SAI-007 — Chat Continuation Guide

Version: 2.0  
Status: Active — Production hardening handoff  
Last Updated: 2026-09-12

---

## Purpose

Use this document to continue Sentinel AI hardening without restarting or repeating completed work.

## Repository State

- Repository: `jab1015/Sentinel-AI`
- Baseline assessment commit: `1218f5d39e2e98f955179d7911b013636068d373`
- Working branch: `security/production-hardening-1218f5d`
- Pre-documentation checkpoint: `d8ac4af17a6451cb4f82e86ce45b64523f1ed303`
- Authoritative finding tracker: `docs/SENTINEL_AI_PRODUCTION_REMEDIATION.md`
- Do not merge to `main` during hardening.

## Required Reading Order

1. `SAI-000_Project_Status.md`
2. `SENTINEL_AI_PRODUCTION_REMEDIATION.md`
3. `SAI-004_Sprint_History.md`
4. `SAI-005_Product_Roadmap.md`
5. `SAI-025_Master_Development_Plan.md`
6. Relevant architecture/testing documents for the finding being changed.

## Current CI Evidence

At `d8ac4af1...`:

- Windows hardening workflow PASS: `34671410981` (and `34671409289` at the same head).
- Package workflow PASS: `34671410865`.
- 10 consecutive BoundedProcessRunner iterations PASS.
- Quarantine adversarial harness PASS.
- Broker package-identity policy harness PASS.
- Authenticode, system-image, cloud-redaction, event-filtering, history, diagnostics, and AI-gateway harnesses PASS.

Do not treat these results as production readiness.

## Current Work Order

1. **A07/A15 broker adversarial runtime review** — test same-user unrelated callers, copied/spoofed binaries, malformed/oversized/extra JSON, protocol/operation abuse, arbitrary paths/commands, PID reuse, target replacement, caller exit during UAC, UAC cancel, broker no-connect/no-send/disconnect/hang, pipe races, second callers, packaged paths, and upgrade state. Package-full-name binding is implemented and CI-tested; installed elevated identity still requires runtime proof.
2. **A19 Ask Sentinel final claim boundary** — `MainWindow` can replace an orchestrator-validated response on optimization/external/driver paths. Put one deterministic validator/provenance boundary immediately before final display after all replacements; add regression tests.
3. **A01 Authenticode** — catalog, self-signed/untrusted, timestamps, revocation/offline, replacement races, cache invalidation, and shipped architectures.
4. **A05 AI gateway** — Google Cloud + Store staging abuse/entitlement/replay/rate/concurrency/outage validation.
5. Continue remaining findings, High re-audit, then all-29 re-audit.
6. Final signed package/runtime/stability qualification.

## Quarantine Handoff

Deterministic CI now covers basic quarantine/restore/delete, collision, tamper, crash recovery, corrupt/forged transactions, forged restore path/temp path, duplicate IDs, and forced metadata cleanup failure. Restore/delete no longer report success when protected metadata cleanup fails; transaction evidence is preserved.

Still open: real standard-user ACL resistance, resulting ACL/owner verification, reparse/junction/symlink/hardlink attacks, source/destination replacement races, orphan states, all crash checkpoints, disk full/access denied, destination directory deletion, broker killed mid-operation, and installed package/UAC behavior.

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
