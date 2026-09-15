# SAI-008 — Release Checklist

Version: 2.4  
Status: REOPENED — latest VM findings require Vault/Explorer UX completion, fresh source CI, package and Windows validation  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Important Status Correction

The historical `1.0.25.0 Release Candidate Validated` state is superseded by the September production-security program. Sentinel AI must not be described as production-hardened or release-qualified until all applicable runtime, external, stability, package, UX and independent-review gates are complete.

Current application/package version: `1.0.27.0`.

## Qualified Hardening Baseline

- [x] Hardening branch isolated: `security/production-hardening-1218f5d`
- [x] Fully qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- [x] 12/12 required hardening source/automated workflows PASS
- [ ] Installed Windows hardening/runtime qualification complete
- [ ] All 14 High findings independently reverified
- [ ] All 15 Medium findings independently reverified
- [ ] Full 29-finding final re-audit complete

## Current Premium Privacy Checkpoint

- [x] Premium Privacy branch isolated: `feature/premium-privacy-foundation`
- [x] Previous exact-head source qualification passed before latest UI/Vault changes
- [ ] Latest implementation head `3cd7a0da195145d03cee3aa947d3f0b9c1d8f69f` exact-head qualification complete
- [ ] Current workflow `34913034860` (#279) reaches terminal PASS
- [x] Encryption replacement foundation and regression coverage implemented
- [x] Portable password retry implemented
- [x] Authenticated decrypt then encrypted-source retirement implemented
- [x] Persistent performance baseline implemented
- [x] Vault cryptographic/storage foundation implemented
- [x] Vault recovery-key copyability correction implemented
- [x] Initial Vault exact-source retirement boundary implemented
- [ ] Vault exact-source retirement acceptance harness passes current-head CI
- [ ] Complete user-facing Vault workflow
- [ ] Installed Windows privacy matrix complete
- [ ] Real provider/runtime evidence complete
- [ ] GCP multi-instance shared-state/staging evidence complete
- [ ] Real Microsoft Store entitlement lifecycle evidence complete

## Latest VM UX / Product Findings

- [ ] Inspect success result uses evidence-accurate security language such as `No issue found` / `No suspicious condition found`
- [ ] Inspect does not claim malware safety unless malware/threat evidence was actually evaluated
- [ ] Explorer Inspect shows only compact useful dialog chrome, not a large empty gray host window
- [ ] Encrypt for This PC shows only compact useful dialog chrome
- [ ] Encrypt for Sharing shows only compact useful dialog chrome
- [ ] Decrypt shows only compact useful dialog chrome
- [ ] Compact dialogs remain usable at Windows DPI/text scaling settings
- [ ] Sentinel dashboard/page layout responds cleanly to compact/normal/wide/maximized window sizes
- [ ] Modern Methods/Sentinel navy/electric-blue/cyan/white visual language is consistently applied
- [ ] Keyboard focus, contrast and accessibility remain acceptable after visual modernization

## Encryption Replacement UX

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
- [ ] all replacement/recovery cases revalidated in the next fresh installed VM package

## Vault Completion

- [x] Vault cryptographic/storage foundation exists
- [x] Vault recovery key can be presented in selectable/copyable control in source
- [x] Vault primary navigation entry added in current source
- [x] Exact-object Vault source-retirement boundary added in current source
- [ ] Vault source-retirement acceptance harness passes exact-head CI
- [ ] Vault page/browser is implemented and launchable from primary navigation
- [ ] Committed Vault items are listed without exposing unnecessary plaintext
- [ ] Add Files works for one/multiple files
- [ ] Successful Add Files performs verified commit first, exact plaintext retirement second
- [ ] Failed/incomplete Add Files preserves readable source and reports incomplete replacement
- [ ] Add Folder is implemented with safe recursive enumeration
- [ ] Add Folder handles reparse points/junctions safely
- [ ] Add Folder provides clear partial-success/failure reporting
- [ ] Vault restore/export workflow is implemented
- [ ] Vault collision behavior validated
- [ ] Vault hard-link/reparse/protected-location/race behavior validated
- [ ] Vault cancellation/crash/recovery behavior validated
- [ ] Existing Vault data remains recoverable under required entitlement-expiry policy
- [ ] Full installed Windows Vault matrix passes

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
- [ ] installed VM restart/cadence behavior confirmed on next fresh package

Manual optimization remains a scan/evaluation action; it must not silently become an apply-remediation action without separate entitlement/safety design.

## Windows VM Test Package

Authoritative handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

The package used for the latest installed VM review is now superseded for release qualification by UI/Vault source changes.

- [x] Package version remains `1.0.27.0`
- [x] Configuration `LocalDev` remains compile-time-only for isolated subscription-free VM testing
- [x] Architecture x64
- [x] Format MSIX
- [x] Production identity/Publisher requirements retained
- [ ] Latest required source workflows pass on the final package source head
- [ ] Current-head dedicated signed package run completes successfully
- [ ] Automated cryptographic signature verification recorded
- [ ] Exact signer certificate/Publisher verification recorded
- [ ] Automated package contents inspection recorded
- [ ] Explorer COM/context-menu packaged-manifest verification recorded
- [ ] Shipped-binary x64 PE verification recorded
- [ ] Final artifact uploaded and independently inspected/downloaded
- [ ] Fresh LocalDev MSIX installed on Windows 11 VM
- [ ] Installed LocalDev runtime matrix complete

Do not weaken cryptographic, identity, package-content, manifest, architecture or entitlement verification to make packaging green.

## Windows Runtime Qualification

- [ ] Clean Windows 11 VM install of next exact artifact
- [ ] Upgrade/uninstall where applicable
- [ ] Standard-user/admin/UAC matrices
- [ ] Installed privileged broker identity and caller rejection
- [ ] Explorer restart/context-menu/single/multi-select/Unicode/long-path behavior
- [ ] Explorer Inspect compact-dialog and evidence-accurate result wording
- [ ] Encrypt for This PC compact UI and verified replacement
- [ ] Encrypt for Sharing compact UI, invalid/mismatched-password retry and verified replacement
- [ ] Local-profile decrypt restores exact plaintext
- [ ] Portable correct-password decrypt restores exact plaintext
- [ ] Wrong portable password re-prompts and preserves container
- [ ] Successful authenticated decrypt retires encrypted source only after plaintext restore
- [ ] Tamper failure preserves encrypted source and does not accept plaintext
- [ ] Older `.senc` compatibility revalidated
- [ ] Destination collision behavior revalidated
- [ ] Vault launch/browser/Add Files/Add Folder/restore/recovery/source-retirement matrix
- [ ] Secure Delete exact-object/race/reparse/hardlink/protected-path/UAC/crash cases
- [ ] Performance baseline survives restart and advances at one accepted sample/minute
- [ ] Responsive dashboard at compact/normal/wide/maximized sizes and Windows scaling
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
- CURRENT PREMIUM PRIVACY EXACT-HEAD CI: **IN PROGRESS — run `34913034860`**
- ENCRYPTION REPLACEMENT: **IMPLEMENTED; fresh qualification required**
- VAULT USER-FACING COMPLETION: **NO**
- RESPONSIVE VISUAL MODERNIZATION: **ACTIVE**
- CURRENT-HEAD SIGNED VM PACKAGE: **NOT YET QUALIFIED**
- WINDOWS VM RUNTIME QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

Only after every applicable gate is satisfied may the branch be labeled `READY FOR FINAL INDEPENDENT REVIEW`. Production release requires that review plus final distribution evidence.

---

End of Document
