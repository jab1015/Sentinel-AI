# SAI-008 — Release Checklist

Version: 2.1  
Status: REOPENED — Windows VM package qualification in progress; production release not authorized  
Last Updated: 2026-09-13

Copyright (c) 2026 Modern Methods.

---

## Important Status Correction

The historical `1.0.25.0 Release Candidate Validated` state is superseded by the September production-security program. Sentinel AI must not be described as production-hardened or release-qualified until all applicable runtime, external, stability, package, and independent-review gates are complete.

Current application/package version under VM preparation: `1.0.26.0`.

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
- [x] Premium Privacy source/CI foundation qualified
- [x] Explorer integration source/CI
- [x] Encryption/recovery source/CI
- [x] Vault source/CI
- [x] Secure Delete logical exact-object execution source/CI
- [x] Broad attributable-copy discovery foundation source/CI
- [x] Premium Privacy entitlement/capability source/CI
- [ ] Installed Windows privacy matrix complete
- [ ] Real provider/runtime evidence complete
- [ ] GCP multi-instance shared-state/staging evidence complete
- [ ] Real Microsoft Store entitlement lifecycle evidence complete

## Windows VM Test Package — ACTIVE BLOCKER

Authoritative handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Package-source checkpoint currently under qualification: `f51a31fe53c64cadd0e346eb32648a86353d23f2`.

Latest packaging run: `34736783747`, job `103669514840`.

- [x] Package version `1.0.26.0`
- [x] Configuration Release
- [x] Architecture x64
- [x] Format MSIX
- [x] Production package identity guard passed
- [x] Unsigned Release x64 MSIX build passed
- [x] Ephemeral dedicated VM test certificate creation passed
- [x] MSIX signing passed
- [x] Temporary signing PFX deleted by workflow
- [x] Production Store signing key not used
- [x] Private signing key not committed
- [ ] Automated cryptographic signature verification passed
- [ ] Exact signer certificate/subject/thumbprint verification passed
- [ ] Package contents inspection passed
- [ ] `Sentinel.App.exe` exactly once
- [ ] `Sentinel.PrivilegedBroker.exe` exactly once
- [ ] `Sentinel.ExplorerExtension.dll` exactly once
- [ ] Required Explorer COM/context-menu registrations verified in packaged manifest
- [ ] Required shipped binaries verified x64 PE machine `0x8664`
- [ ] Final artifact uploaded
- [ ] Public `SentinelAI-TestSigning.cer` included
- [ ] Install/uninstall helpers included
- [ ] `SHA256SUMS.txt` and `PackageBuildInfo.txt` included
- [ ] Final artifact independently checked for absence of `.pfx`, `.p12`, `.key`, private-key PEM, and signing password
- [ ] Final package SHA-256 independently confirmed
- [ ] WINDOWS VM TESTING READY

### Current VM Package Failure

Run `34736783747` successfully built and signed the MSIX. `Verify signed VM test MSIX` then remained active until the 30-minute job timeout and was cancelled. The workflow contains an intended 120-second SignTool bound, therefore the next action is to inspect the full verification log, identify the exact blocked operation, and correct the timeout/verification implementation without weakening cryptographic verification.

Do not substitute an unrelated manual Visual Studio build merely to bypass this gate. The intended result is one reproducible signed test artifact tied to an exact source SHA.

## Windows Runtime Qualification

- [ ] Clean Windows 11 VM install
- [ ] Upgrade
- [ ] Uninstall
- [ ] Standard-user/admin/UAC matrices
- [ ] Installed privileged broker identity and caller rejection
- [ ] Explorer restart/context-menu/single/multi-select/Unicode/long-path behavior
- [ ] Encryption normal/failure/recovery/corruption/crash cases
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

- WINDOWS VM TESTING READY: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- RELEASE QUALIFIED: **NO**

Only after every applicable gate is satisfied may the branch be labeled `READY FOR FINAL INDEPENDENT REVIEW`. Production release requires that review plus final distribution evidence.

---

End of Document
