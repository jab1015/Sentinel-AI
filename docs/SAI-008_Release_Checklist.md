# SAI-008 — Release Checklist

Version: 2.0  
Status: REOPENED — Production hardening and release qualification required  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Important Status Correction

The earlier `1.0.25.0 Release Candidate Validated` statement is historical evidence only. The September production assessment found 29 security/reliability findings (14 High, 15 Medium). Sentinel AI must **not** be described as production-hardened or release-qualified until the gates below are complete.

## Current Internal CI Gates

At hardening checkpoint `d8ac4af17a6451cb4f82e86ce45b64523f1ed303`:

- [x] Desktop x64 Release build
- [x] Privileged broker x64 Release build
- [x] Broker package-identity policy harness
- [x] Authenticode acceptance harness
- [x] BoundedProcessRunner harness — 10 consecutive passes
- [x] Quarantine adversarial harness
- [x] System-image classification harness
- [x] Cloud redaction harness
- [x] Event filtering harness
- [x] Investigation-history harness
- [x] Diagnostic logging harness
- [x] AI gateway security harness
- [x] Full Windows hardening workflow — run `34671410981`
- [x] Unsigned x64 generated-MSIX package gate — run `34671410865`

These checkboxes mean the listed CI gate passed, not that the related production finding is fully closed.

## Security Finding Closure

- [ ] All 14 High findings fully verified
- [ ] All 15 Medium findings fully verified
- [ ] Fresh adversarial re-audit of every High finding
- [ ] Fresh complete re-audit of all 29 findings
- [ ] No unresolved release-blocking findings

## Quarantine Runtime Qualification

- [ ] Standard user cannot modify protected payload/record/transaction/directories
- [ ] Resulting owner/DACL verified, not merely ACL-command exit code
- [ ] Reparse/junction/symlink/hardlink attacks
- [ ] Source replacement/change during quarantine
- [ ] Destination creation/replacement during restore
- [ ] Orphan payload/record/transaction recovery
- [ ] Crash at every transaction checkpoint
- [ ] Disk-full and access-denied behavior
- [ ] Destination-directory deletion/race
- [ ] Broker termination mid-operation
- [ ] Installed package/UAC flow

## Privileged Broker Qualification

- [x] Package-full-name identity policy deterministic tests
- [ ] Installed elevated broker retains expected package identity
- [ ] Same-user unrelated caller rejected
- [ ] Copied/spoofed client/broker rejected
- [ ] Malformed/oversized/extra JSON rejected
- [ ] Unsupported protocol/operation rejected
- [ ] Arbitrary command/path attempts rejected
- [ ] PID reuse and target replacement rejected
- [ ] UAC accept/cancel behavior
- [ ] Client exit/cancel/disconnect/hang behavior
- [ ] Pipe race/second caller behavior
- [ ] Upgrade/pending-package behavior

## Ask Sentinel / AI

- [ ] A19 final displayed-response validator after all replacement/composition paths
- [ ] Regression tests for false blocked/quarantined/repaired/Defender/firewall claims
- [ ] Google Cloud staging authentication/entitlement abuse matrix
- [ ] Store paid-entitlement validation
- [ ] Distributed replay/rate/concurrency/spend controls
- [ ] Provider secret/IAM/Secret Manager validation
- [ ] Deployed log/redaction inspection

## Authenticode / Architecture

- [ ] Embedded trusted signature
- [ ] Catalog-signed trusted binary
- [ ] Unsigned/tampered/self-signed/untrusted
- [ ] Timestamped expired-valid and expired-invalid cases
- [ ] Revoked and offline-revocation behavior
- [ ] Replacement during verification/cache invalidation
- [ ] Every shipped architecture (x64/x86/ARM64 as applicable)

## Store / Package / Windows Runtime

- [ ] Store-signed provenance
- [ ] Clean install
- [ ] Upgrade
- [ ] Uninstall
- [ ] Supported Windows versions
- [ ] Standard-user/admin/UAC matrices
- [ ] StartupTask enable/disable/Windows-disabled state
- [ ] Duplicate activation/Explorer restart
- [ ] Sleep/wake and network loss/recovery
- [ ] Defender/firewall interaction
- [ ] Crash/recovery/failure paths

## Stability / Performance

- [ ] Fresh final-commit 1-hour stability run
- [ ] Fresh final-commit 8-hour stability run
- [ ] CPU/memory/handle/thread/resource trend review
- [ ] UI responsiveness validation

## Final Decision Gate

Current decision: **DO NOT MERGE / NOT RELEASE-QUALIFIED**.

Only after every applicable gate above is satisfied may the branch be labeled `READY FOR FINAL INDEPENDENT REVIEW`. Production release requires that independent review plus final distribution evidence.

---

End of Document
