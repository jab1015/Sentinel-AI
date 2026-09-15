# SAI-000 — Project Status

Version: 4.2  
Status: Active — VM UX/Vault findings under correction and exact-head requalification in progress  
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
- Current implementation head before this documentation update: `3cd7a0da195145d03cee3aa947d3f0b9c1d8f69f`
- Current Premium Privacy workflow: `34913034860` (#279), **IN PROGRESS**
- Current Windows VM package workflow: `34913034850` (#73), **IN PROGRESS/PENDING at last check**
- Current package version: `1.0.27.0`
- Premium Privacy remains isolated from hardening.
- Release posture: **NOT production ready; DO NOT MERGE TO MAIN**

Documentation commits advance the branch beyond implementation checkpoints. Exact source SHA and workflow run must be recorded for every qualification/package.

## Current Phase

The immediate focus is correcting the latest installed-VM UX/Vault findings, requalifying exact-head source, producing a fresh signed LocalDev x64 package, and repeating installed Windows validation.

Current state:

- hardening source/automated qualification: **PASS**;
- previous Premium Privacy source/automated qualification: **PASS**, but superseded by new UI/Vault implementation and therefore exact-head requalification is required;
- encryption replacement source + acceptance coverage: **IMPLEMENTED / previous CI PASS**;
- Explorer compact-action hosting: **IMPLEMENTED; visual refinement now in progress**;
- decryption encrypted-source retirement after authenticated recovery: **IMPLEMENTED / previous CI PASS**;
- portable-password retry UX: **IMPLEMENTED / previous CI PASS**;
- persistent optimization baseline: **IMPLEMENTED / previous CI PASS**;
- Vault cryptographic/storage foundation: **IMPLEMENTED / tested foundation**;
- Vault user-facing management: **NEW UI/workflow implementation in progress; runtime validation pending**;
- current exact-head Premium Privacy CI: **IN PROGRESS**;
- fresh automated signed VM package: **NOT YET QUALIFIED for current head**;
- Windows installed/runtime qualification: **NOT COMPLETE**;
- GCP/Store external qualification: **OPEN**;
- production readiness: **NO**.

## Latest Installed-VM Findings

The current installed VM package confirmed that earlier functional corrections substantially improved Explorer encryption/decryption behavior, but exposed additional product/UX gaps that must be corrected before runtime qualification:

1. **Inspect wording is not security-oriented enough.** A successful folder inspection currently reports that the filesystem object is accessible. The user-facing result should clearly distinguish `No issue found` / `No suspicious condition found` from warnings/errors, without claiming malware safety that Sentinel did not actually verify.
2. **Explorer action chrome is visually excessive.** The white action card is hosted inside a large gray Sentinel window. Explorer actions should feel like compact native Sentinel dialogs, with only the useful branded dialog surface and controls visible.
3. **Vault folder import is missing.** The current workflow only selects files. Folder selection/import must be implemented safely rather than implied by the UI.
4. **Vault add currently leaves the readable original in place.** The product requirement is replacement semantics: encrypt and verify the Vault item first, then retire the exact original source; failure must preserve the source and must not report full success.
5. **Vault has no practical item browser/launcher.** Users need an obvious in-app Vault destination where committed items can be viewed and managed.
6. **Overall application visual design needs modernization.** The dashboard should scale cleanly across window sizes, reduce fixed/legacy-looking layout behavior, and use the Modern Methods/Sentinel navy, electric-blue, cyan and white visual language more consistently.

These are tracked as product defects/improvements, not release-ready behavior.

## Implementation Applied After VM Feedback

The active branch now includes initial corrections for the above findings:

- `VaultSourceRetirementService` added as a fail-closed exact-object source-retirement boundary for Vault replacement semantics;
- a dedicated acceptance harness was added for Vault source-retirement success, missing source, directory rejection, reparse rejection, protected-location rejection, hard-link rejection, identity-change rejection, and normal exact-object retirement;
- the main dashboard XAML was refreshed with responsive `VisualStateManager` breakpoints, Modern Methods/Sentinel blue/navy accents, improved card treatment, stronger title hierarchy, and a visible Vault navigation entry;
- existing Sentinel security functionality and entitlement boundaries remain unchanged by the visual refresh.

The current exact-head workflow must pass before these changes are called source-qualified.

## Explorer / Inspection UX Requirement

Explorer Inspect must report what Sentinel actually established. Acceptable successful language includes `No issue found` or `No suspicious condition found in this inspection`, followed by useful details. It must not convert a simple accessibility check into an unsupported `safe from malware` claim.

Explorer Inspect, Encrypt for This PC, Encrypt for Sharing, Decrypt, Vault entry actions, and Secure Delete confirmations should use compact branded surfaces rather than displaying a large empty application host around a small white dialog.

## Vault Product Requirement

Vault is not complete until all of the following are true:

- Vault is directly launchable from the application navigation;
- committed Vault items are visible in a user-facing browser;
- Add Files supports one or more files;
- Add Folder safely enumerates/imports supported files with clear partial-failure reporting;
- successful import follows **verified encrypted commit first, exact plaintext source retirement second**;
- if encryption/verification/commit/source-retirement fails, Sentinel preserves the readable source and reports that replacement is incomplete;
- restore/export is explicit and recovery remains possible after subscription expiry where required by product policy;
- collision, reparse, hard-link, protected-location, race, cancellation, crash and recovery behavior is tested;
- installed Windows runtime behavior is validated.

Until those gates pass, Vault must continue to be described as **unfinished**.

## Optimization Baseline

The performance baseline persists at `%LOCALAPPDATA%\Modern Methods\Sentinel AI\performance-baseline.json` instead of resetting whenever Sentinel restarts. Existing safety rules remain: 12 accepted samples, at most one accepted sample per minute, 720-sample cap, stale/future/corrupt-state defenses, and sanitization of non-finite live metrics.

Manual optimization remains an evaluation/scan operation; it does not silently apply remediation.

## Windows VM Test Package

Authoritative package handoff: `docs/testing/SAI-WIN-001_VM_Test_Package.md`.

The previous signed LocalDev package was sufficient to expose the latest UI/Vault runtime findings, but it is now superseded for qualification purposes by source changes. A new exact-head signed package is required after current source CI passes.

Package requirements remain unchanged: version `1.0.27.0`, x64, LocalDev compile-time entitlement only, production identity/Publisher retained, signature/signer/package/manifest/PE checks preserved.

## LocalDev Entitlement Rule

The VM build uses compile-time-only `SENTINEL_LOCAL_DEV` authorization. Release/Store continues to require authoritative Microsoft Store + gateway entitlement. No runtime flag, preference, environment variable, hidden switch, or reusable production token may enable the LocalDev bypass in Release.

## Windows Physical Revalidation Required

The next fresh package must validate at minimum:

- compact Explorer action surfaces without the oversized gray host;
- Inspect wording that clearly communicates no issue/suspicious condition found without unsupported malware claims;
- Encrypt for This PC and Encrypt for Sharing verified replacement behavior;
- decrypt password retry, exact plaintext restoration, and encrypted-source retirement;
- tampered-container failure safety;
- Vault launch/navigation and committed-item browser;
- Vault Add Files replacement semantics;
- Vault Add Folder behavior and partial-failure reporting;
- Vault recovery-key copyability;
- responsive/scaling dashboard behavior at small, medium, large and maximized window sizes;
- optimization baseline restart persistence;
- standard/admin/UAC, Defender/firewall, quarantine/recovery, startup/lifecycle, failure behavior, and stability/resource behavior.

## External Validation — Parallel Workstream

Still required before production release:

- shared atomic multi-instance gateway replay/rate/concurrency state;
- GCP staging with Secret Manager, least-privilege IAM, restart/failover, safe logging, alerts, and budget controls;
- real Microsoft Store entitlement lifecycle evidence;
- signed Store provenance/lifecycle evidence.

## Current Decision

- HARDENING SOURCE/AUTOMATED QUALIFICATION: **PASS**
- CURRENT PREMIUM PRIVACY EXACT-HEAD CI: **IN PROGRESS — run `34913034860`**
- ENCRYPTION REPLACEMENT: **IMPLEMENTED; fresh exact-head requalification required**
- PERSISTENT OPTIMIZATION BASELINE: **IMPLEMENTED; fresh exact-head requalification required**
- VAULT SOURCE RETIREMENT BOUNDARY: **IMPLEMENTED; CI/runtime qualification pending**
- VAULT USER-FACING COMPLETION: **NO**
- CURRENT-HEAD AUTOMATED SIGNED VM PACKAGE: **NOT YET QUALIFIED**
- WINDOWS INSTALLED RUNTIME QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**
- PRODUCTION READY: **NO**

---

End of Document
