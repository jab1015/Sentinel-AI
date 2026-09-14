# SAI-008 — Release Checklist

Version: 2.3  
Status: REOPENED — Premium Privacy source/CI qualified; signed LocalDev package and installed Windows validation remain open  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Important Status Correction

The historical `1.0.25.0 Release Candidate Validated` state is superseded by the September production-security program. Sentinel AI must not be described as production-hardened or release-qualified until all applicable runtime, external, stability, package, and independent-review gates are complete.

Current application/package version: `1.0.27.0`.

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
- [x] Exact-head Premium Privacy CI passed at `8899a9d8afa04056b2131a989f4cd6f1c832f801`
- [x] Workflow run `34909736208` (#271) PASS
- [x] Explorer integration source/CI
- [x] Compact Explorer-action dialog source correction/CI
- [x] Local-profile encryption/decryption source/CI
- [x] Portable password-protected `Encrypt for Sharing...` source/CI
- [x] Portable password retry source correction/CI
- [x] Verified encryption replacement source/CI
- [x] Authenticated decrypt then encrypted-source retirement source/CI
- [x] Vault source foundation/CI
- [x] Vault recovery-key copyability source correction/CI
- [x] Secure Delete logical exact-object execution source/CI
- [x] Broad attributable-copy discovery foundation source/CI
- [x] Premium Privacy entitlement/capability source/CI
- [x] Persistent performance-baseline source/CI
- [ ] Installed Windows privacy matrix complete
- [ ] Complete user-facing Vault workflow
- [ ] Real provider/runtime evidence complete
- [ ] GCP multi-instance shared-state/staging evidence complete
- [ ] Real Microsoft Store entitlement lifecycle evidence complete

### Encryption Replacement UX

The approved replacement behavior is now implemented and acceptance-covered:

- [x] `Encrypt for This PC` retires plaintext only after verified successful encryption
- [x] `Encrypt for Sharing...` retires plaintext only after verified successful encryption
- [x] no `Keep original` choice is presented
- [x] encryption failure leaves the source untouched
- [x] finalization/verification failure leaves the source untouched
- [x] full success is not reported when source retirement is incomplete
- [x] `.senc` decrypts after source removal in automated acceptance coverage
- [x] wrong portable password fails safely in automated coverage
- [x] tampered encrypted data fails safely in automated coverage
- [x] older `.senc` files remain backward-compatible in automated coverage
- [x] destination collisions never silently overwrite in automated coverage
- [ ] all above replacement/recovery cases revalidated in the fresh installed VM package

## Optimization Baseline Reliability

- [x] Baseline persistence implemented under Local AppData
- [x] 12-sample establishment threshold preserved
- [x] one accepted sample per minute cadence preserved
- [x] restart persistence acceptance-covered
- [x] corrupt persisted state fails closed to relearning
- [x] stale persisted history beyond 24 hours discarded
- [x] future history beyond five-minute skew rejected
- [x] invalid future history cannot suppress new legitimate samples
- [x] non-finite live metrics sanitized
- [ ] installed VM restart/cadence behavior confirmed

Manual optimization remains a scan/evaluation action; it must not silently become an apply-remediation action without separate entitlement/safety design.

## Windows VM Test Package

Authoritative handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Current dedicated signed-package run: `34908583736` (#69), source `a12af475b7212edf1d78e37fbcf07c9fb0bc34dd`.

- [x] Package version `1.0.27.0`
- [x] Configuration `LocalDev` available for isolated subscription-free VM testing
- [x] Architecture x64
- [x] Format MSIX
- [x] Compile-time LocalDev entitlement boundary verified in CI
- [x] Authoritative Release x64 entitlement path compiled in CI
- [x] Latest exact-head Premium Privacy workflow `34909736208` passed
- [ ] Current dedicated signed package run completes successfully
- [ ] Automated cryptographic signature verification recorded for the final artifact
- [ ] Exact signer certificate/Publisher verification recorded for the final artifact
- [ ] Automated package contents inspection recorded for the final artifact
- [ ] Explorer COM/context-menu packaged-manifest verification recorded for the final artifact
- [ ] Shipped-binary x64 PE verification recorded for the final artifact
- [ ] Final artifact uploaded and independently inspected/downloaded
- [ ] Fresh LocalDev MSIX installed on Windows 11 VM
- [ ] Installed LocalDev runtime matrix complete

Earlier run `34901580179` (#68) was superseded after its build/sign/qualify step had succeeded, but artifact upload was cancelled. This does not substitute for a terminal successful run with a downloadable artifact.

Do not weaken cryptographic, identity, package-content, manifest, architecture, or entitlement verification to make packaging green.

## Windows Runtime Qualification

- [ ] Clean Windows 11 VM install
- [ ] Upgrade/uninstall where applicable
- [ ] Standard-user/admin/UAC matrices
- [ ] Installed privileged broker identity and caller rejection
- [ ] Explorer restart/context-menu/single/multi-select/Unicode/long-path behavior
- [ ] Explorer Inspect opens compact dialog only, not full dashboard
- [ ] Encrypt for This PC opens compact UI and performs verified replacement
- [ ] Encrypt for Sharing opens compact UI, retries invalid/mismatched passwords, and performs verified replacement
- [ ] Local-profile decrypt restores exact plaintext
- [ ] Portable correct-password decrypt restores exact plaintext
- [ ] Wrong portable password re-prompts and preserves container
- [ ] Successful authenticated decrypt retires encrypted source only after plaintext restore
- [ ] Tamper failure preserves encrypted source and does not accept plaintext
- [ ] Older `.senc` compatibility revalidated
- [ ] Destination collision behavior revalidated
- [ ] Vault recovery key selectable/copyable
- [ ] Vault lifecycle/recovery/crash/concurrency cases
- [ ] Secure Delete exact-object/race/reparse/hardlink/protected-path/UAC/crash cases
- [ ] Performance baseline survives restart and advances at one accepted sample/minute
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

- HARDENING SOURCE/AUTOMATED: **PASS**
- PREMIUM PRIVACY SOURCE/CI: **PASS at `8899a9d8...` / run `34909736208`**
- ENCRYPTION REPLACEMENT SOURCE/CI: **PASS**
- OPTIMIZATION BASELINE PERSISTENCE SOURCE/CI: **PASS**
- FRESH AUTOMATED SIGNED VM PACKAGE: **IN PROGRESS — run `34908583736`**
- WINDOWS VM RUNTIME QUALIFICATION: **NOT COMPLETE**
- COMPLETE USER-FACING VAULT: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

Only after every applicable gate is satisfied may the branch be labeled `READY FOR FINAL INDEPENDENT REVIEW`. Production release requires that review plus final distribution evidence.

---

End of Document
