# SAI-WIN-001 — Windows VM Test Package

Version: 1.2  
Status: LOCALDEV SOURCE BUILD PASS — AUTOMATED SIGNED PACKAGE QUALIFICATION STILL OPEN  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Purpose

This document is the handoff and evidence record for producing and validating the isolated signed Sentinel AI Windows 11 VM test package. It is not Microsoft Store or production-release authorization.

## Current Source

- Repository: `jab1015/Sentinel-AI`
- Branch: `feature/premium-privacy-foundation`
- Exact source checkpoint used for the current manual LocalDev build: `c0b60c06e4f03f9486965c799f7af028dd450a05`
- Package version: `1.0.27.0`
- Architecture: `x64`
- Configuration: `LocalDev`
- Package format: `MSIX`
- Production identity retained: `ModernMethods.SentinelAI`
- Publisher retained: `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`
- Production Store signing key used for LocalDev test package: **NO**

Documentation commits after this checkpoint do not change which source SHA the current manual build came from.

## Exact-Head CI Evidence

Premium Privacy workflow run `34792512987` against `c0b60c06e4f03f9486965c799f7af028dd450a05`: **PASS**.

That run qualified the Premium Privacy source foundation, including Explorer acceptance, file-encryption/Secure Delete acceptance, privacy discovery, gateway entitlement coverage, native Explorer builds for x64/x86/ARM64, desktop app build, gateway build, unsigned x64 package creation, and packaged Explorer-extension verification.

Dedicated Windows VM package workflow run `34792512975` against the same SHA:

- LocalDev entitlement-boundary verification: PASS
- Release x64 entitlement-path compile: PASS
- Build/sign/qualify LocalDev x64 VM package: remained active until workflow cancellation
- Overall conclusion: **CANCELLED**

Therefore automated signed-package qualification is still open. Do not relabel that workflow as passing.

## Manual Visual Studio Evidence

On 2026-09-13, source `c0b60c06...` was pulled locally and Visual Studio was configured as:

- Configuration: `LocalDev`
- Platform: `x64`

`Build -> Rebuild Solution` completed with:

`2 succeeded, 0 failed, 1 up-to-date, 0 skipped`

The user then entered the Visual Studio `Create App Packages` signing wizard. The existing selected certificate showed Publisher-compatible subject `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`, SHA256 signing, and expiration in 2027.

This manual package is for isolated VM testing only. It is not Store/production evidence and does not replace the still-open automated signed-package qualification problem.

## Test-Only Subscription Behavior

The Windows VM package is intentionally compiled with `LocalDev`. `Sentinel.App.csproj` defines `SENTINEL_LOCAL_DEV` only for that configuration.

`PremiumPrivacyEntitlementClient` contains a compile-time `#if SENTINEL_LOCAL_DEV` path permitting supported Premium Privacy scopes for isolated VM validation without an active Store subscription. Release/Store builds compile the authoritative Microsoft Store + Sentinel gateway flow.

This bypass must remain impossible to enable through a runtime preference, environment variable, hidden UI toggle, or reusable production token.

## Packaging Requirements

The final VM package must preserve:

- package version `1.0.27.0`;
- identity `ModernMethods.SentinelAI`;
- Publisher `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`;
- x64 architecture;
- exactly one each of `Sentinel.App.exe`, `Sentinel.PrivilegedBroker.exe`, and `Sentinel.ExplorerExtension.dll`;
- Explorer COM/context-menu registration with CLSID `6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21`;
- `windows.comServer` and `windows.fileExplorerContextMenus` manifest registrations.

## Encryption UX Requirement for VM Validation

The current source checkpoint still leaves the original plaintext file untouched after creating `.sentinel.senc`. That behavior has now been rejected for the intended user experience.

Required next implementation:

- `Encrypt for This PC` and `Encrypt for Sharing...` must create and verify the `.sentinel.senc` file first;
- only after verified success, automatically remove the plaintext source;
- no `Keep original` option should be shown;
- any encryption/finalization/verification failure must leave the plaintext source intact;
- removal of the plaintext source must not damage or invalidate the self-contained `.senc` container;
- existing older `.senc` files must remain decryptable.

Do not treat this requirement as implemented until source, regression tests, and qualification are updated.

## Required Runtime Tests

For the installed LocalDev package, validate at minimum:

1. install/upgrade/uninstall;
2. launch and startup behavior;
3. Explorer context-menu registration and restart behavior;
4. `Encrypt for This PC`;
5. `Encrypt for Sharing...` with password confirmation;
6. decrypt local-profile container;
7. decrypt portable container with correct password;
8. wrong password failure;
9. tamper failure;
10. plaintext source-removal behavior after the pending UX change;
11. source preservation on encryption failure;
12. Vault lifecycle/recovery;
13. Secure Delete;
14. standard-user/admin/UAC behavior;
15. Defender/firewall interaction;
16. quarantine/recovery and crash/failure behavior;
17. stability/resource behavior.

## Current Readiness

- PREMIUM PRIVACY EXACT-HEAD CI: **PASS at c0b60c06...**
- MANUAL LOCALDEV X64 REBUILD: **PASS**
- MANUAL LOCALDEV MSIX CREATION: **IN PROGRESS**
- AUTOMATED SIGNED VM PACKAGE QUALIFICATION: **NO / CANCELLED**
- SUBSCRIPTION REQUIRED IN LOCALDEV VM PACKAGE: **NO**
- SUBSCRIPTION REQUIRED IN RELEASE/STORE BUILD: **YES**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**

---

End of Document
