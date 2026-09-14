# SAI-000 — Project Status

Version: 4.1  
Status: Active — Premium Privacy exact-head CI qualified; fresh signed LocalDev VM-package qualification in progress  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Single Source of Truth

Sentinel AI remains in the production-security hardening program established from assessment commit `1218f5d39e2e98f955179d7911b013636068d373`.

- Production branch: `main`
- Hardening branch: `security/production-hardening-1218f5d`
- Fully qualified hardening checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Hardening automated state: **12/12 required source/automated workflows PASS**
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Latest source/test checkpoint before this documentation synchronization: `8899a9d8afa04056b2131a989f4cd6f1c832f801`
- Premium Privacy workflow run `34909736208` (#271): **PASS**
- Current package version: `1.0.27.0`
- Premium Privacy remains isolated from hardening.
- Release posture: **NOT production ready; DO NOT MERGE TO MAIN**

Documentation commits may advance the branch beyond the source-qualified checkpoint. Record the exact source SHA used for every package separately from the live documentation head.

## Current Phase

The current focus is completing the signed LocalDev x64 VM package and then re-running installed Windows 11 VM validation against the corrected Explorer/privacy/optimization behavior.

Current state:

- hardening source/automated qualification: **PASS**;
- Premium Privacy exact-head source/automated qualification: **PASS** at `8899a9d8...` via run `34909736208`;
- encryption replacement source + acceptance coverage: **IMPLEMENTED / CI PASS**;
- Explorer compact-dialog source corrections: **IMPLEMENTED / CI PASS; installed runtime revalidation pending**;
- decryption encrypted-source retirement after authenticated recovery: **IMPLEMENTED / CI PASS; installed runtime revalidation pending**;
- portable-password retry UX: **IMPLEMENTED / CI PASS; installed runtime revalidation pending**;
- Vault recovery-key copyability correction: **IMPLEMENTED / CI PASS; installed runtime revalidation pending**;
- persistent optimization baseline: **IMPLEMENTED / CI PASS**;
- fresh automated signed VM package qualification: **IN PROGRESS**;
- Windows installed/runtime revalidation: **NOT COMPLETE**;
- GCP/Store external qualification: **OPEN**;
- production readiness: **NO**.

## Exact-Head Premium Privacy Qualification

Run `34909736208` (#271) completed successfully against `8899a9d8afa04056b2131a989f4cd6f1c832f801`.

That run passed Explorer integration acceptance, file-encryption and Secure Delete acceptance, persistent performance-baseline acceptance, privacy discovery, gateway entitlement acceptance, native Explorer x64/x86/ARM64 builds, desktop app build, gateway build, unsigned x64 package creation, and packaged Explorer-extension x64 verification.

The gateway tamper fixture was made deterministic before this pass by mutating decoded authenticated signature bytes rather than a potentially non-significant Base64URL padding-bit representation. This was a test-fixture correction, not a relaxation of capability validation.

## Encryption Replacement — Implemented

Both `Encrypt for This PC` and `Encrypt for Sharing...` now follow the approved replacement model:

1. create the `.sentinel.senc` container;
2. complete authenticated encryption/finalization;
3. verify the encrypted result;
4. only after verified success retire the plaintext source;
5. do not offer a `Keep original` choice;
6. preserve plaintext on encryption, finalization, verification, cancellation, or source-retirement failure;
7. do not report full success if replacement is incomplete;
8. preserve collision protection and older-container compatibility.

Acceptance coverage includes portable round-trip, failure, unverified-result rejection, cancellation, collision, source-retirement failure, and legacy v1 decryption.

## Explorer / Recovery Runtime Corrections

Source changes made from the prior VM findings include:

- initial and redirected Explorer activations no longer intentionally surface the full Sentinel dashboard;
- Explorer actions use a compact dedicated dialog host;
- `Encrypt for Sharing...` re-prompts after too-short, empty, or mismatched passwords;
- decryption retries portable passwords after key-unavailable failure;
- authenticated decryption restores plaintext first and only then attempts exact-object logical retirement of the encrypted source;
- failure to safely prove encrypted-source retirement leaves the restored plaintext and encrypted source in place and reports the partial result;
- recovery cleanup is not blocked by Premium entitlement expiry;
- Vault recovery key is displayed in a selectable read-only text box for copy/paste.

These changes are source/CI qualified but still require fresh installed VM confirmation.

## Optimization Baseline Correction

The performance baseline now persists at `%LOCALAPPDATA%\Modern Methods\Sentinel AI\performance-baseline.json` instead of resetting whenever Sentinel restarts.

Safety/quality rules remain:

- 12 accepted samples establish a baseline;
- at most one accepted sample per minute;
- maximum 720 retained samples;
- stale persisted samples beyond 24 hours are discarded;
- future samples beyond five minutes of clock skew are rejected;
- corrupt/invalid persisted state fails closed to relearning;
- non-finite live metrics are sanitized;
- a rejected future persisted sample cannot suppress new legitimate observations.

Manual optimization remains an evaluation/scan operation and does not silently apply optimization changes. Automatic execution remains subject to subscription, safety, state, lease, and cooldown rules.

## Windows VM Test Package

Authoritative package handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

Dedicated workflow: `.github/workflows/windows-vm-test-package.yml`.

Package run `34908583736` (#69) against source `a12af475b7212edf1d78e37fbcf07c9fb0bc34dd` is currently the active signed LocalDev x64 package qualification run. It has passed checkout/tool discovery, the compile-time LocalDev entitlement boundary, and authoritative Release x64 entitlement-path compilation; its build/sign/qualify package step is still in progress as of this synchronization.

An earlier run `34901580179` (#68) was superseded/cancelled after its build/sign/qualify package step itself had already completed successfully, but artifact upload did not complete. That evidence is useful for diagnosis but is not a qualified downloadable package result.

Do not weaken package, signer, identity, architecture, or cryptographic verification to obtain a green workflow.

## LocalDev Entitlement Rule

The VM build uses compile-time-only `SENTINEL_LOCAL_DEV` authorization. Release/Store continues to require authoritative Microsoft Store + gateway entitlement. No runtime flag, preference, environment variable, hidden switch, or reusable production token may enable the LocalDev bypass in Release.

## Windows Physical Revalidation Required

Once the fresh signed package is available, validate at minimum:

- install/upgrade/uninstall;
- Explorer Inspect opens compact dialog only, not the full dashboard;
- `Encrypt for This PC` opens compact UI and removes plaintext only after verified replacement;
- `Encrypt for Sharing...` opens compact UI, retries invalid/mismatched passwords, and performs verified replacement;
- decryption retries wrong portable passwords, restores exact plaintext, and retires the encrypted source only after authenticated success;
- tampered containers fail safely without accepted plaintext or premature encrypted-source removal;
- Vault recovery key can be selected/copied;
- optimization baseline survives restart and progresses at the intended one-sample-per-minute cadence;
- standard/admin/UAC, Defender/firewall, quarantine/recovery, startup/lifecycle, failure behavior, and stability/resource behavior.

## Vault Status

Sentinel Vault has substantial cryptographic/storage/backend foundation and acceptance coverage. The complete user-facing Vault workflow is **not finished** and must not be described as complete.

## External Validation — Parallel Workstream

Still required before production release:

- shared atomic multi-instance gateway replay/rate/concurrency state;
- GCP staging with Secret Manager, least-privilege IAM, restart/failover, safe logging, alerts, and budget controls;
- real Microsoft Store entitlement lifecycle evidence;
- signed Store provenance/lifecycle evidence.

## Current Decision

- HARDENING SOURCE/AUTOMATED QUALIFICATION: **PASS**
- PREMIUM PRIVACY SOURCE/CI: **PASS at `8899a9d8...` / run `34909736208`**
- ENCRYPTION REPLACEMENT SOURCE/CI: **PASS**
- PERSISTENT OPTIMIZATION BASELINE SOURCE/CI: **PASS**
- FRESH AUTOMATED SIGNED VM PACKAGE: **IN PROGRESS**
- WINDOWS INSTALLED RUNTIME REVALIDATION: **NOT COMPLETE**
- VAULT USER-FACING COMPLETION: **NO**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- PRODUCTION READY: **NO**

---

End of Document
