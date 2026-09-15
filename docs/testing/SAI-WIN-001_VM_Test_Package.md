# SAI-WIN-001 — Windows VM Test Package

Version: 1.4  
Status: INSTALLED VM FEEDBACK CAPTURED — CURRENT UI/VAULT SOURCE REQUALIFICATION AND FRESH PACKAGE REQUIRED  
Last Updated: 2026-09-14

Copyright (c) 2026 Modern Methods.

---

## Purpose

This document is the authoritative handoff and evidence record for producing and validating the isolated signed Sentinel AI Windows 11 VM test package. It is not Microsoft Store or production-release authorization.

## Current Source and Package Rules

- Repository: `jab1015/Sentinel-AI`
- Branch: `feature/premium-privacy-foundation`
- Current implementation checkpoint before documentation updates: `3cd7a0da195145d03cee3aa947d3f0b9c1d8f69f`
- Current Premium Privacy workflow: `34913034860` (#279), **IN PROGRESS**
- Current Windows VM package workflow: `34913034850` (#73), **current-head run started/pending at last check**
- Package version: `1.0.27.0`
- Architecture: `x64`
- Configuration: `LocalDev`
- Package format: `MSIX`
- Production identity retained: `ModernMethods.SentinelAI`
- Publisher retained: `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`
- Production Store signing key used for LocalDev test package: **NO**

Documentation commits advance the branch beyond implementation checkpoints. Package qualification must always record the exact packaged source SHA.

## Test-Only Subscription Behavior

The Windows VM package is intentionally compiled with `LocalDev`. `SENTINEL_LOCAL_DEV` remains compile-time-only. Release/Store builds continue to compile the authoritative Microsoft Store + Sentinel gateway entitlement flow.

The LocalDev behavior must remain impossible to enable through a runtime preference, environment variable, hidden UI toggle, command-line switch, or reusable production token.

## Latest Installed VM Feedback

The installed package confirmed that the previously reported full-dashboard activation issue was functionally reduced to a dedicated action host, and the encryption/decryption flows are substantially improved. New runtime feedback identifies these remaining gaps:

- Inspect result currently emphasizes that a folder is an accessible directory rather than giving a clear security-oriented result.
- Explorer action UI still appears as a white dialog inside a large gray Sentinel window. The desired result is the compact useful dialog surface only.
- Vault cannot yet add a folder.
- Vault file import leaves the readable original in place, which violates the desired replacement behavior for a successful Vault move.
- There is no practical Vault browser/launcher where users can see/manage committed Vault items.
- The overall application needs a more modern responsive visual system using Modern Methods/Sentinel navy, electric blue, cyan and white branding.

These findings mean the installed package is **not runtime-qualified**.

## Inspect Result Requirement

A successful Inspect result should use clear language such as `No issue found` or `No suspicious condition found in this inspection` and then explain what was verified.

Sentinel must not say a file/folder is malware-safe unless the inspection actually performed and passed the relevant malware/threat checks. Accessibility/filesystem verification alone is not sufficient for a broad `safe` claim.

## Compact Explorer Dialog Requirement

Inspect, Encrypt for This PC, Encrypt for Sharing, Decrypt, Vault actions and Secure Delete confirmations should not show a large empty gray host around the task UI. The target is a compact branded native Sentinel dialog/window that contains only the necessary content and controls and remains usable with Windows DPI/text scaling.

## Vault Replacement Requirement

For successful Add Files/Add Folder operations, Vault must follow the same safety principle as encryption replacement:

1. encrypt the Vault item;
2. durably commit and verify the encrypted Vault state;
3. bind/revalidate the exact plaintext source object;
4. retire the readable source only after the encrypted commit is proven usable;
5. verify source absence;
6. report full success only when both the Vault commit and source retirement complete.

If source retirement cannot be safely proven, preserve the source and report incomplete replacement. Never delete plaintext before verified Vault commit.

Current source includes a new `VaultSourceRetirementService` and acceptance harness covering exact-object normal retirement plus missing-source, directory, reparse, protected-location, hard-link and identity-change rejection. Exact-head CI/runtime qualification remains pending.

## Vault User-Facing Requirement

The next package is not considered Vault-complete until it provides:

- obvious Vault navigation/launch;
- committed-item browser/list;
- Add Files;
- Add Folder;
- refresh/empty/progress/error states;
- verified source-retirement semantics;
- restore/export/recovery workflow;
- recovery-key copy/paste;
- safe collision, reparse, hard-link, protected-location, cancellation, crash and recovery behavior.

## Visual/Responsive Requirement

The main app should scale more like a responsive modern application while remaining native WinUI 3. Current source begins this work with `VisualStateManager` breakpoints, refreshed navigation/card styling, stronger Sentinel blue/navy accents and a visible Vault navigation entry.

The VM must test compact, normal, wide and maximized layouts plus Windows DPI/text scaling. Visual modernization may not weaken accessibility, native Windows behavior, security-state clarity or existing functionality.

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

Do not weaken or bypass signing, Publisher/signer verification, package-content inspection, Explorer manifest verification, x64 PE verification or entitlement-boundary verification.

## Required Next Installed VM Revalidation

Using the next fresh qualified LocalDev x64 package, validate:

1. clean install and launch;
2. Explorer context-menu registration after Explorer restart;
3. Inspect opens compact task UI only and reports evidence-accurate `No issue found`/warning/error state;
4. Encrypt for This PC compact UI and verified replacement;
5. Encrypt for Sharing compact UI, password validation/retry and verified replacement;
6. local-profile and portable decrypt restore exact plaintext;
7. wrong portable password retry and tamper failure safety;
8. successful decrypt encrypted-source retirement;
9. Vault launches from primary navigation;
10. Vault browser displays committed items;
11. Add Files performs verified Vault commit then exact source retirement;
12. Add Folder imports supported files safely and reports partial failures;
13. Vault restore/export/recovery and recovery-key copyability;
14. Vault reparse/hard-link/protected-location/race/collision/cancellation/crash cases;
15. responsive dashboard at compact/normal/wide/maximized sizes and Windows scaling;
16. optimization baseline restart/cadence behavior;
17. standard-user/admin/UAC behavior;
18. Defender/firewall interaction;
19. quarantine/recovery and crash/failure behavior;
20. startup/background and stability/resource behavior.

## Current Readiness

- HARDENING SOURCE/AUTOMATED QUALIFICATION: **PASS**
- CURRENT PREMIUM PRIVACY EXACT-HEAD CI: **IN PROGRESS — run `34913034860`**
- ENCRYPTION REPLACEMENT: **IMPLEMENTED; fresh exact-head qualification required**
- VAULT SOURCE-RETIREMENT BOUNDARY: **IMPLEMENTED; qualification pending**
- VAULT USER-FACING COMPLETION: **NO**
- RESPONSIVE VISUAL MODERNIZATION: **ACTIVE**
- CURRENT-HEAD SIGNED LOCALDEV X64 PACKAGE: **NOT YET QUALIFIED**
- SUBSCRIPTION REQUIRED IN LOCALDEV VM PACKAGE: **NO**
- SUBSCRIPTION REQUIRED IN RELEASE/STORE BUILD: **YES**
- WINDOWS INSTALLED RUNTIME QUALIFICATION: **NOT COMPLETE**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**

---

End of Document
