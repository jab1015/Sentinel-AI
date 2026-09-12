# SAI-PRIV-003 — Sentinel Vault Architecture

Status: DESIGN COMPLETE — IMPLEMENTATION PENDING  
Version: 1.0  
Date: 2026-09-12

## Purpose

Define a vault architecture that isolates item keys, minimizes plaintext exposure, supports independent recovery, and locks predictably.

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

## Lock state

The service has explicit states:

- Locked
- Unlocking
- Unlocked
- Locking
- RecoveryRequired
- Corrupt

No operation may infer `Unlocked` merely because a prior operation succeeded.

## Unlock modes

Supported planned modes:

- Windows current-user
- password
- independent recovery key

Future Windows Hello/TPM integration is a separate reviewed feature and must not silently change recovery semantics.

## Automatic lock

Lock on:

- configured inactivity timeout
- Windows session lock
- explicit user lock
- app shutdown
- security-sensitive state transition where key retention cannot be justified

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

## Add-item transaction

`validate source -> create encrypted item temp -> encrypt/flush -> reopen/authenticate -> commit encrypted item -> commit metadata -> optionally offer separate plaintext removal`

The original file remains if any encryption/verification step fails.

## Open-item transaction

`authenticate vault -> locate exact item -> unwrap item DEK -> authenticate metadata -> stream decrypt`

No unauthenticated plaintext is exposed.

## Concurrency

- one writer transaction per vault
- bounded concurrent readers only where key lifetime and state transitions remain safe
- cross-process exclusive lease for metadata mutations
- item IDs are random and immutable
- package upgrade must not bypass vault locking or recovery state

## Crash recovery

Durable transaction state must distinguish at least:

- Prepared
- CiphertextReady
- MetadataCommitted
- CleanupPending
- Complete

Recovery is idempotent and fails closed when state is ambiguous.

## Logging

Never log VMKs, item DEKs, passwords, recovery keys, decrypted filenames/paths unless a separately reviewed privacy policy permits a bounded sanitized representation.

## Required tests

- create/open/add
- explicit lock/unlock
- wrong password
- Windows-account mode
- recovery key
- inactivity timeout
- Windows session lock
- abrupt termination at every transaction phase
- corrupted/missing metadata
- package upgrade
- concurrent access
- temp-plaintext cleanup and cleanup failure
- vault copy/move behavior
- stale lock/lease recovery

## Qualification state

DESIGN COMPLETE only. No vault operation is production-qualified until cryptographic implementation, crash recovery, session-lock integration, package runtime, and independent review are complete.
