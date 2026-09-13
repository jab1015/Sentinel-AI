# SAI-WIN-001 — Windows VM Test Package

Version: 1.1  
Status: LOCALDEV VM PACKAGE IMPLEMENTED — CURRENT SIGNED QUALIFICATION RUN IN PROGRESS  
Last Updated: 2026-09-13

Copyright (c) 2026 Modern Methods.

---

## Purpose

This document is the handoff and evidence record for producing the isolated signed Sentinel AI Windows 11 VM test package. It is not a production-release or Microsoft Store authorization.

## Current Source

- Repository: `jab1015/Sentinel-AI`
- Branch: `feature/premium-privacy-foundation`
- LocalDev VM package implementation checkpoint before this documentation synchronization: `36f8dafda6e11ed7234440f7009e5b3be266c32d`
- Package version: `1.0.28.0`
- Architecture: `x64`
- Configuration: `LocalDev`
- Package format: `MSIX`
- Expected test package name: `SentinelAI-WindowsVM-x64.msix`
- Test public certificate: `SentinelAI-TestSigning.cer`
- Production Store signing key used: **NO**
- Private key committed: **NO**
- Private key artifact published: **NO**

## Test-Only Subscription Behavior

The Windows VM package is intentionally compiled with the existing `LocalDev` configuration. `Sentinel.App.csproj` defines `SENTINEL_LOCAL_DEV` only for that configuration.

`PremiumPrivacyEntitlementClient` has a compile-time `#if SENTINEL_LOCAL_DEV` authorization path that allows the supported Premium Privacy scopes for isolated VM validation without requiring an active Microsoft Store subscription:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

The VM authorization result is visibly identified as `LocalVmTestAuthorized` and uses a `LOCAL-VM-TEST-...` token identifier.

This is **not** a runtime preference, environment-variable bypass, hidden UI toggle, or reusable production token. Release/Store builds compile the normal authoritative Microsoft Store + Sentinel gateway flow and still require real entitlement verification.

The dedicated VM workflow separately compiles the Release x64 application before creating the LocalDev package so accidental removal or breakage of the authoritative Release entitlement path is detected.

## Packaging Workflow

Workflow: `.github/workflows/windows-vm-test-package.yml`

The workflow:

1. verifies the compile-time LocalDev entitlement boundary and continued presence of the authoritative Store/gateway path;
2. compiles the Release x64 app as a regression gate;
3. builds the Windows Application Packaging Project in `LocalDev / x64 / SideloadOnly` mode;
4. maps the native Explorer extension to its qualified `Release` native configuration because the C++ project intentionally has no LocalDev configuration and contains no entitlement logic;
5. creates an ephemeral runner-only RSA 3072 Code Signing certificate whose subject matches the existing package Publisher;
6. exports only the public `.cer` to the artifact;
7. signs the MSIX and deletes the temporary PFX;
8. verifies the package signature and signer;
9. unpacks and verifies package contents, manifest registrations and x64 PE architecture;
10. stages install/uninstall helpers and SHA-256 metadata;
11. refuses to publish private signing material.

Production package identity remains unchanged:

- Name: `ModernMethods.SentinelAI`
- Publisher: `CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A`

## Required Package Contents

The final package must contain exactly one of each:

- `Sentinel.App.exe`
- `Sentinel.PrivilegedBroker.exe`
- `Sentinel.ExplorerExtension.dll`

The packaged manifest must preserve:

- `windows.comServer`
- `windows.fileExplorerContextMenus`
- `Sentinel.ExplorerExtension.dll`
- CLSID `6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21`
- `Type="*"`
- `Type="Directory"`

## Current Qualification Attempt

Dedicated workflow run: `34777838659`  
Source SHA: `36f8dafda6e11ed7234440f7009e5b3be266c32d`  
State at documentation update: **IN PROGRESS**

Do not mark the package VM-ready until this run completes successfully and publishes the signed artifact.

## Required Success Evidence

Before installation begins require:

- compile-time LocalDev entitlement boundary: PASS
- Release x64 entitlement path compile: PASS
- LocalDev x64 MSIX build: PASS
- test signature creation: PASS
- signature verification: PASS
- signer Publisher match: PASS
- `Sentinel.App.exe`: present and x64
- `Sentinel.PrivilegedBroker.exe`: present and x64
- `Sentinel.ExplorerExtension.dll`: present and x64
- Explorer COM/context-menu manifest registration: PASS
- SHA-256 published
- public `.cer` published
- install/uninstall helpers published
- no PFX/private key/password in artifact

## Installation Rule

For this VM test package, use **LocalDev**, not Release.

- `LocalDev`: subscription-free isolated Windows testing.
- `Release`: real Microsoft Store + Sentinel gateway entitlement remains required.

A LocalDev package must never be submitted to the Microsoft Store or treated as production evidence for subscription enforcement.

## Current Readiness

- WINDOWS VM TESTING READY: **PENDING CURRENT SIGNED PACKAGE WORKFLOW**
- SUBSCRIPTION REQUIRED IN LOCALDEV VM PACKAGE: **NO**
- SUBSCRIPTION REQUIRED IN RELEASE/STORE BUILD: **YES**
- PRODUCTION STORE READY: **NO**
- MERGE TO MAIN: **NO**

---

End of Document
