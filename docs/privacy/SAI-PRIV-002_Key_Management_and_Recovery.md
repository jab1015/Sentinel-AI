# SAI-PRIV-002 — Key Management and Recovery

Status: DESIGN COMPLETE — IMPLEMENTATION PENDING  
Version: 1.0  
Date: 2026-09-12

## Goals

Sentinel separates content encryption from key protection. Every encrypted file receives its own random 256-bit DEK. The DEK is wrapped independently for one or more authorized recovery modes; file content is never re-encrypted merely because a key-wrapping mode changes.

## Key hierarchy

### Standalone encrypted file

`Random File DEK -> AES-256-GCM file content`

The same DEK may be wrapped by one or more independent key records:

- Windows current-user protection
- password-derived key-encryption key (KEK)
- independent recovery key
- future vault item wrapping

No raw DEK is persisted.

### Sentinel Vault

`Vault Master Key -> wraps independent per-item DEKs -> item DEK encrypts one item`

The Vault Master Key never directly encrypts arbitrary file content.

## Windows-account mode

- Uses current-user Windows protection for the independent file DEK.
- Does not use machine-wide protection by default.
- The protected blob is stored as an authenticated wrapped-key record.
- Failure to unwrap under the active Windows user is a normal access-denied result, not corruption by itself.
- A Windows-account wrapped DEK does not silently grant another account access.

Implementation must prefer supported Windows cryptographic protection APIs and remain package-compatible. DPAPI/current-user behavior must be tested under standard and administrator accounts, account changes, profile migration, package upgrade, and unavailable profile conditions.

## Password mode

Password mode derives a KEK with a reviewed memory-hard KDF.

Preferred algorithm: Argon2id, only through a maintained, reviewable library suitable for commercial distribution.

Required per-record parameters:

- random salt of at least 16 bytes
- algorithm identifier
- versioned memory cost
- versioned iteration/time cost
- versioned parallelism
- wrapping algorithm identifier

The password is never stored. The derived KEK is held only as long as required and cleared where the runtime permits reliable clearing.

The KEK wraps the independent DEK using an authenticated key-wrapping construction. AES-GCM may be used for DEK wrapping only with a fresh random wrapping nonce that is stored in the authenticated key-record parameters.

Password verification is implicit in successful authenticated DEK unwrap; no plaintext password verifier is stored.

## Recovery key

Each recovery-enabled file/vault receives independent high-entropy recovery material generated with a cryptographically secure RNG.

Requirements:

- minimum 256 bits of random secret material
- human transport encoding includes version and checksum
- display groups are formatting only and do not reduce entropy
- Copy, Save, and Print are explicit user actions
- recovery secret is never silently uploaded, synced, logged, or sent to Sentinel cloud services
- app diagnostics must redact recovery material
- recovery material wraps the DEK or Vault Master Key through a dedicated authenticated wrapping record

Before an `Encrypt + Secure Delete Original` action becomes available, the UI must make recovery configuration and consequences clear and must not imply that Sentinel can recover a lost secret when no alternate key record exists.

## Recovery-key textual format

Version 1 proposed representation:

`SAI-RK1-<base32 secret>-<checksum>`

The checksum detects transcription errors only; it is not an authenticator and does not add entropy.

## Key-record independence

A container may have multiple authorized wrapped-key records. Removing one record requires rewriting and re-authenticating the container header; it must not mutate ciphertext chunks.

At least one valid key record must remain after any key-management operation.

## Memory handling

- Avoid managed string representation for passwords/recovery secrets beyond unavoidable UI boundaries.
- Prefer `Span<byte>`, `byte[]`, or secure OS credential surfaces for cryptographic material.
- Zero temporary key buffers with `CryptographicOperations.ZeroMemory` where meaningful.
- Do not log exception objects that may embed user-entered secrets.
- Do not include secrets in command-line arguments, protocol URIs, Explorer handoff files, or broker messages.

## Cloud boundary

Encryption keys and recovery keys are local. Existing server-authoritative subscription checks control feature access but are not key escrow and must never receive reusable encryption secrets.

## Failure semantics

Key unwrap returns structured states such as:

- Success
- WrongCredentialOrAccount
- UnsupportedKeyRecord
- CorruptKeyRecord
- ProtectionProviderUnavailable
- Canceled

No key-mode failure authorizes plaintext deletion or fallback to a weaker mode.

## Required tests

- correct Windows account
- wrong Windows account
- password correct/wrong
- recovery key correct/wrong
- corrupt wrapped DEK
- corrupt parameters
- duplicate key records
- unknown key mode
- missing key records
- password/recovery material absent from logs
- package upgrade
- concurrent unlock attempts
- cancellation
- memory/exception path review

## Qualification state

DESIGN COMPLETE only. Library selection for Argon2id and implementation of Windows account, password, recovery, and Vault key wrapping require separate source/test commits and independent cryptographic review.
