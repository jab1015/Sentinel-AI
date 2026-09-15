# SAI-PRIV-000 — File Explorer Integration Architecture

Status: SOURCE IN PROGRESS — WINDOWS/PACKAGE VALIDATION REQUIRED  
Version: 1.0  
Date: 2026-09-12

## Purpose

Define the trust boundary for Sentinel AI File Explorer commands. Explorer integration is a convenience activation surface, not a security authority.

## Supported architecture

Sentinel uses a packaged native `IExplorerCommand` COM DLL registered through the MSIX package with:

- `windows.comServer` / COM surrogate registration
- `windows.fileExplorerContextMenus`
- `*` selected-file registration
- `Directory` selected-folder registration

The native DLL is built for every shipped Explorer architecture: x86/Win32, x64, and ARM64.

## P1 command

The only P1 command is:

`Inspect with Sentinel AI`

P1 deliberately does not expose:

- Encrypt File
- Add to Sentinel Vault
- Secure Delete
- privacy cleanup

Those verbs remain gated on later feature qualification.

## Shell-extension responsibilities

The native Explorer DLL may only:

1. enumerate a bounded selection of filesystem-backed shell items;
2. collect `SIGDN_FILESYSPATH` values;
3. create a bounded one-time handoff record inside Sentinel package LocalState;
4. activate Sentinel AI with a random handoff identifier;
5. return promptly.

The DLL must not:

- scan files;
- hash files;
- perform network/cloud calls;
- validate subscriptions;
- carry entitlement tokens or secrets;
- invoke the privileged broker;
- encrypt/decrypt;
- delete/overwrite files;
- make security conclusions.

## Handoff format

P1 handoff record:

- protocol version 1
- command exactly `inspect`
- creation timestamp
- 1–16 filesystem paths
- maximum serialized record 64 KiB
- random 128-bit GUID filename/token

Unknown JSON fields fail closed in the app.

## Important same-user tampering boundary

The LocalState handoff record is **not trusted authorization**. Another process running as the same Windows user may be able to modify user-owned package data depending on Windows/package boundaries and runtime conditions.

Therefore:

- the app treats every handoff field as untrusted input;
- the handoff command is restricted to harmless `inspect` in P1;
- Sentinel re-canonicalizes and confirms each filesystem path exists before displaying/processing it;
- the handoff is consumed once and deleted;
- stale, malformed, oversized, unknown-field, missing-item, and destructive-command records fail closed;
- no future destructive or cryptographic operation may use a handoff record as authorization.

Future Encrypt/Vault/Secure Delete flows must require fresh in-app user intent/policy, server-authoritative entitlement where required, exact-object safety validation, and broker-side independent revalidation for privileged mutations.

## Stable-object identity

P1 path existence/canonicalization is sufficient only for a harmless inspection activation. It is **not** stable object identity and must never be reused to authorize a destructive action.

Later privacy actions must reopen/bind the exact filesystem object and defend against target replacement, reparse, junction, link, parent replacement, and UAC-time races.

## Activation lifecycle

The Explorer DLL activates the packaged app using the package family AppUserModelId and passes only:

`--sentinel-explorer-handoff <random-guid-token>`

Sentinel's existing `AppInstance` single-instance boundary redirects activation to the primary instance. The receiving instance consumes the one-time record and then shows the inspection UI.

## P1 qualification requirements

Source/deterministic:

- strict handoff parser
- destructive-command rejection
- stale/oversized/item-count rejection
- one-time consumption
- strict unknown-field rejection
- manifest CLSID consistency
- package references native DLL
- no forbidden cloud/broker/crypto/delete logic in native DLL
- x86/x64/ARM64 native builds
- x64 MSIX contains x64 Explorer DLL

Windows/package runtime:

- clean install
- context menu appears for files and folders
- single select
- multi-select
- unsupported shell item
- long path
- Unicode
- Explorer restart
- extension/surrogate crash
- app unavailable
- package upgrade
- uninstall
- standard user

## Qualification state

The P1 source foundation is in progress. It is not TESTED or SOURCE COMPLETE until the dedicated Windows/package workflow passes and installed Explorer runtime behavior is validated.
