# SAI-004 — Sprint History

Version: 2.0  
Status: Active  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Historical Foundation

Earlier sprints established WinUI 3, MonitoringEngine/SystemSnapshot, native CPU/memory/disk/network/process monitoring, Defender/Firewall evidence, investigation UX, approval-gated remediation, persistent history, optimization transparency, and Store packaging.

## Production Hardening Program — September 2026

Baseline assessment: `1218f5d39e2e98f955179d7911b013636068d373`  
Assessment result: 29 findings — 14 High, 15 Medium.  
Working branch: `security/production-hardening-1218f5d`.

### Hardening milestone — process reliability

- Centralized bounded subprocess execution.
- Concurrent stdout/stderr drains.
- Wall-clock timeout and cancellation.
- Output caps and structured outcomes.
- Descendant process-tree termination.
- Production PowerShell argument-path coverage.
- Windows CI now repeats the BoundedProcessRunner acceptance harness 10 consecutive times.
- Exact-head checkpoint `d8ac4af1...`: 10x gate PASS.

### Hardening milestone — quarantine

- Added protected quarantine store/recovery semantics.
- Added semantic validation of recovery transactions before destructive recovery.
- Recovery now refuses ambiguous/forged state and preserves evidence.
- Corrected Windows handle rename buffer semantics.
- Strengthened restore identity/hash/link/path verification.
- Added adversarial cases for forged restore path/temp path, corrupt records/transactions, collisions, payload tamper, crash recovery, and duplicate IDs.
- Corrected false-success cleanup behavior: protected record deletion must succeed before transaction deletion; cleanup failures return `MetadataCleanupFailed` and preserve recovery state.
- Deterministic Windows record-lock fault tests PASS.

### Hardening milestone — package and broker

- Unsigned x64 package CI builds/unpacks the generated MSIX and proves the desktop app and privileged broker are both present.
- Corrected installed-runtime validator to use package identity `ModernMethods.SentinelAI`, manifest publisher identity, and actual PackageFamilyName.
- Broker retains allowlisted versioned IPC, current-user pipe restriction, peer PID validation, exact target start/path/hash checks, and cancellation/timeout fail-closed behavior.
- Added Windows package-full-name binding so copied/unpackaged or mismatched broker/client identities are rejected.
- Added broker identity policy harness; same package accepted, different/missing/unpackaged identity rejected.

### Exact-head CI checkpoint

Commit before documentation synchronization: `d8ac4af17a6451cb4f82e86ce45b64523f1ed303`.

- Windows hardening workflow: PASS (`34671410981`; `34671409289` also passed at the same head).
- Package workflow: PASS (`34671410865`).
- Quarantine, Authenticode, broker identity, 10x bounded process, system-image, redaction, event filtering, history, diagnostics, and AI gateway harnesses: PASS.

## Active Sprint — Adversarial Runtime and Final Claim Boundaries

Next work:

1. SAI-A07/A15 packaged broker/UAC hostile-caller and lifecycle testing.
2. SAI-A19 final Ask Sentinel response validation after all UI replacement/composition paths.
3. SAI-A01 extended Authenticode fixture/runtime matrix.
4. SAI-A05 Google Cloud + Microsoft Store entitlement staging validation.
5. Remaining High/Medium findings and complete adversarial re-audit.
6. Signed package/install/update/uninstall and long-duration stability qualification.

## Standing Rule

Green CI is evidence, not a production claim. Source-complete items remain open until their required Windows, Store, cloud, adversarial, and release gates are satisfied.

---

End of Document
