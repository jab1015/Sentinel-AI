# SAI-008 — Release Checklist

Version: 2.2  
Status: REOPENED — LocalDev Windows testing underway; production release not authorized  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Important Status Correction

The historical `1.0.25.0 Release Candidate Validated` state is superseded by the September production-security program. Sentinel AI must not be described as production-hardened or release-qualified until all applicable runtime, external, stability, package, and independent-review gates are complete.

Current application/package version under VM preparation: `1.0.27.0`.

## Qualified Hardening Baseline

- [x] Hardening branch isolated: `security/production-hardening-1218f5d`
- [x] Fully qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- [x] 12/12 required hardening source/automated workflows PASS
- [ ] Installed Windows hardening/runtime qualification complete
- [ ] All 14 High findings independently reverified
- [ ] All 15 Medium findings independently reverified
- [ ] Full 29-finding final re-audit complete

## Premium Privacy Source Qualification

- [x] Premium Privacy branch isolated: `feature/premium-privacy-foundation`
- [x] Exact-head Premium Privacy CI passed at `c0b60c06e4f03f9486965c799f7af028dd450a05`
- [x] Explorer integration source/CI
- [x] Local-profile encryption/decryption source/CI
- [x] Portable password-protected `Encrypt for Sharing...` source/CI
- [x] Vault source foundation/CI
- [x] Secure Delete logical exact-object execution source/CI
- [x] Broad attributable-copy discovery foundation source/CI
- [x] Premium Privacy entitlement/capability source/CI
- [ ] Approved encryption replacement UX implemented and regression-qualified
- [ ] Installed Windows privacy matrix complete
- [ ] Real provider/runtime evidence complete
- [ ] GCP multi-instance shared-state/staging evidence complete
- [ ] Real Microsoft Store entitlement lifecycle evidence complete

### Encryption Replacement UX — REQUIRED NEXT SOURCE CHANGE

Current qualified source leaves the plaintext input in place after creating `.sentinel.senc`. Product decision as of 2026-09-14:

- [ ] `Encrypt for This PC` automatically removes the plaintext source after verified successful encryption
- [ ] `Encrypt for Sharing...` automatically removes the plaintext source after verified successful encryption
- [ ] no `Keep original` choice is presented
- [ ] encryption failure leaves the source untouched
- [ ] finalization/verification failure leaves the source untouched
- [ ] `.senc` decrypts correctly after source removal
- [ ] wrong portable password fails safely
- [ ] tampered encrypted data fails safely
- [ ] older `.senc` files remain backward-compatible
- [ ] destination collisions never silently overwrite user data

Do not mark these complete until implemented and tested.

## Windows VM Test Package

Authoritative handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Exact source checkpoint used for the current manual LocalDev build: `c0b60c06e4f03f9486965c799f7af028dd450a05`.

- [x] Package version `1.0.27.0`
- [x] Configuration `LocalDev` available for isolated subscription-free VM testing
- [x] Architecture x64
- [x] Format MSIX
- [x] Compile-time LocalDev entitlement boundary verified in CI
- [x] Authoritative Release x64 entitlement path compiled in CI
- [x] Exact-head Premium Privacy workflow `34792512987` passed
- [x] Manual Visual Studio `LocalDev | x64` rebuild passed: `2 succeeded, 0 failed, 1 up-to-date, 0 skipped`
- [ ] Manual LocalDev MSIX creation completed and recorded
- [ ] Manual LocalDev MSIX installed on Windows 11 VM
- [ ] Installed LocalDev runtime matrix complete
- [ ] Automated dedicated signed VM-package workflow completes successfully
- [ ] Automated cryptographic signature verification passed
- [ ] Exact signer certificate/subject/thumbprint verification passed
- [ ] Automated package contents inspection passed
- [ ] Automated Explorer COM/context-menu packaged-manifest verification passed
- [ ] Automated shipped-binary x64 PE verification passed
- [ ] Automated final artifact uploaded and independently inspected

Latest automated VM package run `34792512975` on `c0b60c06...` was **cancelled** after its build/sign/qualify package step remained active until the workflow limit. This remains an automation defect/gate to diagnose separately; do not weaken cryptographic or package verification simply to turn it green.

## Windows Runtime Qualification

- [ ] Clean Windows 11 VM install
- [ ] Upgrade from existing 1.0.27.0 VM installation where applicable
- [ ] Uninstall
- [ ] Standard-user/admin/UAC matrices
- [ ] Installed privileged broker identity and caller rejection
- [ ] Explorer restart/context-menu/single/multi-select/Unicode/long-path behavior
- [ ] Encryption normal/failure/recovery/corruption/crash cases
- [ ] Portable cross-profile/machine sharing behavior
- [ ] Plaintext replacement behavior after approved UX source change
- [ ] Vault lifecycle/recovery/crash/concurrency cases
- [ ] Secure Delete exact-object/race/reparse/hardlink/protected-path/UAC/crash cases
- [ ] File History / Previous Versions / Search / Recent / Jump Lists / OneDrive discovery behavior
- [ ] Defender/firewall interaction
- [ ] Startup/background/sleep/wake/network-loss behavior
- [ ] Quarantine/restore/crash/recovery adversarial cases
- [ ] Every architecture actually intended to ship validated at runtime

## Cloud / Store External Qualification

- [ ] Shared atomic multi-instance replay/rate/concurrency state
- [ ] GCP staging with Secret Manager and least-privilege IAM
- [ ] Restart/failover behavior
- [ ] Safe deployed logging/redaction
- [ ] Alerts and budget controls
- [ ] Real Microsoft Store active entitlement
- [ ] Inactive/expired/revoked entitlement
- [ ] Store/network unavailable behavior
- [ ] Signed Store-style provenance/lifecycle evidence

## Stability / Performance

- [ ] Fresh final-commit 1-hour stability run
- [ ] Fresh final-commit 8-hour stability run
- [ ] CPU/memory/handle/thread/resource trend review
- [ ] UI responsiveness validation

## Final Decision Gate

Current decision:

- PREMIUM PRIVACY SOURCE/CI: **PASS at c0b60c06...**
- MANUAL LOCALDEV X64 REBUILD: **PASS**
- WINDOWS VM RUNTIME QUALIFICATION: **NOT COMPLETE**
- AUTOMATED SIGNED VM PACKAGE QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

Only after every applicable gate is satisfied may the branch be labeled `READY FOR FINAL INDEPENDENT REVIEW`. Production release requires that review plus final distribution evidence.

---

End of Document
