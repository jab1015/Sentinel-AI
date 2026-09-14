# SAI-WIN-001 — Windows VM Test Package

Version: 1.3  
Status: SOURCE/CI QUALIFIED — FRESH AUTOMATED SIGNED LOCALDEV PACKAGE QUALIFICATION IN PROGRESS  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Purpose

This document is the authoritative handoff and evidence record for producing and validating the isolated signed Sentinel AI Windows 11 VM test package. It is not Microsoft Store or production-release authorization.

## Current Source and Package Rules

- Repository: `jab1015/Sentinel-AI`
- Branch: `feature/premium-privacy-foundation`
- Latest source/test-qualified checkpoint before this documentation synchronization: `8899a9d8afa04056b2131a989f4cd6f1c832f801`
- Latest Premium Privacy qualification run: `34909736208` (#271), **PASS**
- Active dedicated VM package run: `34908583736` (#69)
- Package run source SHA: `a12af475b7212edf1d78e37fbcf07c9fb0bc34dd`
- Package version: `1.0.27.0`
- Architecture: `x64`
- Configuration: `LocalDev`
- Package format: `MSIX`
- Production identity retained: `ModernMethods.SentinelAI`
- Publisher retained: `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`
- Production Store signing key used for LocalDev test package: **NO**

The source/test commits after `a12af475...` add acceptance/workflow-test corrections and do not change the shipping app payload being packaged by run #69. Documentation commits may advance the branch further and must not be mistaken for a different packaged source SHA.

## Exact-Head Premium Privacy Evidence

Premium Privacy workflow run `34909736208` (#271) completed successfully against `8899a9d8afa04056b2131a989f4cd6f1c832f801`.

It passed:

- Explorer integration acceptance;
- file-encryption and Secure Delete acceptance;
- persistent performance-baseline acceptance;
- privacy discovery acceptance;
- gateway entitlement/capability acceptance;
- native Explorer x64/x86/ARM64 builds;
- Sentinel desktop app build;
- Sentinel gateway build;
- unsigned x64 package build;
- packaged Explorer-extension presence and x64 PE verification.

## Dedicated Signed VM Package Evidence

Workflow: `.github/workflows/windows-vm-test-package.yml`.

Run `34908583736` (#69), source `a12af475b7212edf1d78e37fbcf07c9fb0bc34dd`, is the current fresh signed-package qualification. At this synchronization it has passed:

- checkout/tool discovery;
- compile-time LocalDev entitlement-boundary verification;
- authoritative Release x64 entitlement-path compilation.

Its `Build, sign, and qualify LocalDev x64 VM package` step remains in progress. Do not call the package qualified until the workflow reaches terminal success and uploads the final artifact.

Earlier run `34901580179` (#68) was superseded/cancelled after its build/sign/qualify step itself completed successfully; artifact upload was cancelled. That proves the package script and signer-verification path can complete, but it is not a downloadable qualified artifact.

Do not weaken or bypass signing, Publisher/signer verification, package-content inspection, Explorer manifest verification, or x64 PE verification to turn CI green.

## Test-Only Subscription Behavior

The Windows VM package is intentionally compiled with `LocalDev`. `Sentinel.App.csproj` defines `SENTINEL_LOCAL_DEV` only for that configuration.

`PremiumPrivacyEntitlementClient` uses compile-time `#if SENTINEL_LOCAL_DEV` behavior for isolated VM validation without an active Store subscription. Release/Store builds compile the authoritative Microsoft Store + Sentinel gateway entitlement flow.

This bypass must remain impossible to enable through a runtime preference, environment variable, hidden UI toggle, command-line switch, or reusable production token.

## Packaging Requirements

The final VM package must preserve:

- package version `1.0.27.0`;
- identity `ModernMethods.SentinelAI`;
- Publisher `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`;
- x64 architecture;
- exactly one each of `Sentinel.App.exe`, `Sentinel.PrivilegedBroker.exe`, and `Sentinel.ExplorerExtension.dll`;
- Explorer COM/context-menu registration with CLSID `6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21`;
- `windows.comServer` and `windows.fileExplorerContextMenus` manifest registrations;
- cryptographic package signature and expected signer/Publisher relationship.

## Encryption Replacement — Implemented and Acceptance Covered

The previously rejected plaintext-duplicate behavior has been corrected in source.

For both `Encrypt for This PC` and `Encrypt for Sharing...`:

- create and finalize the `.sentinel.senc` container first;
- verify the encrypted result;
- only after verified success retire the original plaintext source;
- do not offer a `Keep original` choice;
- preserve plaintext on encryption/finalization/verification/cancellation failure;
- do not report full success when source retirement is incomplete;
- never silently overwrite collisions;
- preserve legacy `.senc` decryption compatibility.

Acceptance coverage includes successful portable round-trip, encryption failure, unverified-result rejection, cancellation, collision, source-retirement failure, and legacy v1 decryption.

## Runtime Defects Corrected in Source — Revalidation Required

The previous VM package exposed several runtime/UX defects. Source corrections now exist for:

- Explorer Inspect opening the full dashboard;
- Encrypt for This PC / Sharing opening the full dashboard;
- portable password prompts not retrying short/empty/mismatched values;
- decryption leaving the encrypted `.sentinel.senc` after successful authenticated recovery;
- portable wrong-password retry behavior;
- Vault recovery key not being selectable/copyable;
- optimization baseline resetting after app restart.

These are source/CI qualified but are **not yet installed-runtime qualified**.

## Optimization Baseline Runtime Expectation

The performance baseline persists under `%LOCALAPPDATA%\Modern Methods\Sentinel AI\performance-baseline.json` and should:

- accept at most one baseline sample per minute;
- require 12 accepted samples to establish the baseline;
- survive Sentinel restart;
- discard stale history beyond 24 hours;
- reject samples more than five minutes in the future;
- fail closed to relearning for corrupt persisted state;
- continue accepting legitimate new samples after invalid future state is rejected.

Manual optimization is an evaluation/scan and should not silently apply remediation. Automatic optimization remains subject to entitlement and safety controls.

## Required Installed VM Revalidation

Using the fresh qualified LocalDev x64 package, validate:

1. clean install and launch;
2. upgrade/uninstall where applicable;
3. Explorer context-menu registration after Explorer restart;
4. Inspect opens compact dialog only, without surfacing the full Sentinel dashboard;
5. `Encrypt for This PC` creates a verified container and removes plaintext only after verified success;
6. encryption failure preserves plaintext;
7. `Encrypt for Sharing...` retries too-short/empty/mismatched passwords and performs verified replacement;
8. local-profile decrypt restores exact plaintext;
9. portable decrypt with correct password restores exact plaintext;
10. wrong portable password re-prompts and leaves container intact;
11. successful authenticated decrypt retires the exact encrypted source only after restore succeeds;
12. tamper failure preserves encrypted source and does not accept plaintext;
13. destination collisions never silently overwrite;
14. older `.senc` remains decryptable;
15. Vault recovery key can be selected/copied;
16. optimization baseline survives restart and advances on the intended cadence;
17. standard-user/admin/UAC behavior;
18. Defender/firewall interaction;
19. quarantine/recovery and crash/failure behavior;
20. startup/background and stability/resource behavior.

## Vault Limitation

The Vault cryptographic/storage foundation is substantial and acceptance-covered, but the complete user-facing Vault workflow is not finished. VM observations must not be used to claim Vault product completion.

## Current Readiness

- HARDENING SOURCE/AUTOMATED QUALIFICATION: **PASS**
- PREMIUM PRIVACY SOURCE/CI: **PASS at `8899a9d8...`, run `34909736208`**
- ENCRYPTION REPLACEMENT SOURCE/CI: **PASS**
- PERSISTENT OPTIMIZATION BASELINE SOURCE/CI: **PASS**
- FRESH AUTOMATED SIGNED LOCALDEV X64 PACKAGE: **IN PROGRESS — run `34908583736`**
- SUBSCRIPTION REQUIRED IN LOCALDEV VM PACKAGE: **NO**
- SUBSCRIPTION REQUIRED IN RELEASE/STORE BUILD: **YES**
- WINDOWS INSTALLED RUNTIME REVALIDATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**

---

End of Document
