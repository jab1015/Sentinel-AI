# SAI-PRIV-003 — Sentinel Vault Architecture

Status: IN PROGRESS — KEY-SESSION FOUNDATION SOURCE IMPLEMENTED  
Version: 1.1  
Date: 2026-09-12

## Purpose

Define a vault architecture that isolates item keys, minimizes plaintext exposure, supports independent recovery, and locks predictably.

## Current implementation checkpoint

Source now includes the first non-persistent Vault key/session foundation in `SentinelVaultService`:

- random 256-bit Vault Master Key (VMK)
- VMK protection through one to three unique existing key modes: Windows current-user, password, and independent recovery key
- an authenticated master-key envelope binding vault ID and wrapped VMK records
- explicit Locked / Unlocking / Unlocked / Locking states
- explicit lock, inactivity lock, and a Windows-session-lock hook
- a lock epoch that invalidates an unlock already in progress if a lock/session-lock transition occurs before unlock completes
- random independent 256-bit per-item DEKs
- AES-256-GCM wrapping of each item DEK under the VMK
- Vault ID + immutable Item ID bound into the item-key wrapping AAD
- key leases that zero plaintext item-key buffers when disposed

Deterministic acceptance coverage now exercises envelope tamper rejection, wrong credential rejection, independent item DEKs, item-ID binding, explicit/inactivity/session locking, and a controlled session-lock-during-unlock race.

This is **not yet a persistent usable Vault**. Durable metadata, item ciphertext storage, transaction/recovery records, cross-process writer leasing, app session-notification wiring, and safe item-open/add UI remain to be implemented and runtime-qualified.

## Key hierarchy

`Vault Master Key (VMK) -> wraps per-item DEKs -> each DEK encrypts one item`

Rules:

- The VMK never directly encrypts arbitrary user-file content.
- Every vault item has an independent random 256-bit DEK.
- Item DEKs are authenticated when wrapped by the VMK.
- Rewrapping the VMK does not require re-encrypting every item.
- Compromise/corruption of one item DEK must not cryptographically couple unrelated item ciphertext.

## Vault metadata

A versioned vault header stores only the minimum metadata required to open the vault. Sensitive item metadata should be encrypted under a metadata key derived from or wrapped by the VMK.

Do not store original full paths in plaintext by default.

Required durable metadata includes:

- vault format version
- vault ID
- key-protection records
- item identifiers
- authenticated item metadata records
- transaction/recovery state

Current source implements only the authenticated VMK envelope. Durable item metadata remains **PLANNED**.

## Lock state

The service has explicit states:

- Locked
- Unlocking
- Unlocked
- Locking
- RecoveryRequired
- Corrupt

No operation may infer `Unlocked` merely because a prior operation succeeded.

The current key-session source invalidates in-flight unlock attempts when a lock epoch changes. This specifically prevents a password/recovery/Windows unwrap operation that began before a Windows session lock from completing afterward and silently returning the Vault to `Unlocked`.

## Unlock modes

Source key-protection modes now available to the Vault foundation:

- Windows current-user
- password using bounded Argon2id parameters plus authenticated AES-GCM DEK wrapping
- independent recovery key

Future Windows Hello/TPM integration is a separate reviewed feature and must not silently change recovery semantics.

## Automatic lock

Lock on:

- configured inactivity timeout
- Windows session lock
- explicit user lock
- app shutdown
- security-sensitive state transition where key retention cannot be justified

Current source implements the inactivity transition and session-lock service hook. Wiring the hook to real Windows session notifications remains **WINDOWS RUNTIME / APP INTEGRATION REQUIRED**.

On lock:

- reject new plaintext operations
- allow in-flight operations only if their transaction policy explicitly permits safe completion
- clear VMK/item keys from retained buffers where meaningful
- close plaintext-bearing streams/handles

## Plaintext handling

Preferred design: decrypt through streams directly to the consuming operation without creating persistent plaintext temp files.

If a temporary plaintext file is unavoidable:

- create it only inside a dedicated restricted directory
- use exclusive creation and restrictive ACLs
- record it in a cleanup transaction before plaintext is written
- keep its lifetime minimal
- never place it in generic `%TEMP%` without additional protection
- verify cleanup
- report cleanup failure as `REMAINS`
- recover cleanup state after restart/crash

A cleanup failure must never be hidden by a successful vault-close result.

No Vault plaintext extraction/temp-file implementation exists yet.

## Add-item transaction

`validate source -> create encrypted item temp -> encrypt/flush -> reopen/authenticate -> commit encrypted item -> commit metadata -> optionally offer separate plaintext removal`

The original file remains if any encryption/verification step fails.

Status: **PLANNED**. The independent item-key wrapping primitive is source implemented; the durable add-item transaction is not.

## Open-item transaction

`authenticate vault -> locate exact item -> unwrap item DEK -> authenticate metadata -> stream decrypt`

No unauthenticated plaintext is exposed.

Status: **PLANNED**. Item DEK authenticated unwrap is source implemented; durable item lookup/metadata and streaming open are not.

## Concurrency

- one writer transaction per vault
- bounded concurrent readers only where key lifetime and state transitions remain safe
- cross-process exclusive lease for metadata mutations
- item IDs are random and immutable
- package upgrade must not bypass vault locking or recovery state

The current in-memory state gate and lock epoch protect key-session transitions only. Cross-process durable writer leasing remains **PLANNED**.

## Crash recovery

Durable transaction state must distinguish at least:

- Prepared
- CiphertextReady
- MetadataCommitted
- CleanupPending
- Complete

Recovery is idempotent and fails closed when state is ambiguous.

Status: **PLANNED**.

## Logging

Never log VMKs, item DEKs, passwords, recovery keys, decrypted filenames/paths unless a separately reviewed privacy policy permits a bounded sanitized representation.

Current Vault key-session source does not log key material.

## Required tests

Source/deterministic tests now present:

- authenticated VMK envelope creation/unlock
- wrong key protector
- envelope authenticator tamper
- explicit lock
- independent item DEKs
- item-key ciphertext tamper
- immutable item-ID binding
- inactivity lock
- Windows session-lock service hook
- session lock while unlock is deliberately paused in flight

Still required:

- persistent create/open/add
- wrong password at full Vault UI flow
- Windows-account runtime mode
- recovery key full Vault flow
- actual Windows session notification integration
- abrupt termination at every durable transaction phase
- corrupted/missing durable metadata
- package upgrade
- cross-process concurrent access
- temp-plaintext cleanup and cleanup failure if temporary plaintext is ever introduced
- vault copy/move behavior
- stale lock/lease recovery

## Qualification state

**SOURCE FOUNDATION IMPLEMENTED — CI/RUNTIME VALIDATION REQUIRED.**

This does not qualify Sentinel Vault for user-facing release. No Vault operation is production-qualified until durable cryptographic metadata/storage, crash recovery, Windows session-lock integration, package runtime, adversarial tests, and independent privacy/cryptographic review are complete.
