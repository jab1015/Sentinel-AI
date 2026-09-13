# SAI-PRIV-004 — Secure Delete Design

Status: EXACT-TARGET FOUNDATION + COORDINATOR + RETAINED-HANDLE LEASE + DURABLE JOURNAL CI VERIFIED — DESTRUCTIVE EXECUTOR NOT IMPLEMENTED  
Version: 1.4  
Date: 2026-09-12

## Purpose

Define a storage-aware, exact-target privacy removal architecture that maximizes safe recovery resistance without damaging unrelated Windows or user data and without claiming certainty that cannot be proven.

## Non-goals

Secure Delete is not:

- a whole-drive sanitize command
- a firmware secure-erase UI
- a free-form privileged `DeletePath(string path)` primitive
- permission to erase unrelated history, restore points, or system databases
- permission to modify `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys`
- proof that physical NAND cells no longer contain prior data

## Components

- `SecureDeleteTargetValidator`
- `SecureDeleteCoordinator`
- `StorageCapabilityDetector`
- `SecureDeleteMutationLeaseManager` / `SecureDeleteMutationLease`
- `SecureDeleteOperationJournal`
- future exact-handle mutation executor / narrow privileged broker operation
- future `RelatedArtifactDiscoveryService`

## Current source implementation

Implemented on `feature/premium-privacy-foundation`:

- `SecureDeleteContracts` defines explicit validation and storage-capability result semantics.
- `SecureDeleteTargetValidator` is a non-destructive exact-target boundary. It requires a fully qualified filesystem path, reopens the exact object with reparse traversal disabled, rejects directories/reparse objects/multiple hard links/device namespaces/protected locations/system-critical files, resolves the final handle path, and binds stable volume/file identity.
- Directory inspection uses Windows backup-semantics only so directory objects can be identified and rejected explicitly; this does not create destructive directory authority.
- `SecureDeleteTargetValidator.Revalidate` proves the same volume/file ID and canonical path again; path replacement loses authorization.
- `StorageCapabilityDetector` reports only mounted-volume/root/filesystem/location facts that can be obtained without guessing. Physical media type, BitLocker state, TRIM/unmap support, and cloud synchronization remain `Unknown` until reviewed platform/provider APIs provide trustworthy evidence.
- `SecureDeleteCoordinator` is a non-destructive authorization/preflight boundary. It accepts only a previously validated `SecureDeleteTargetIdentity`, never an arbitrary path string.
- Coordinator authorization is short-lived (currently five minutes), bound to the exact target and storage boundary, and currently limited to local fixed storage.
- Coordinator authorization permits only a future logical-removal path. `AllowsOverwriteSanitization` is explicitly `false` until media-specific overwrite strategy is separately qualified.
- `RevalidateForMutation` rejects expired/malformed authorizations, path/object replacement, unsupported storage, changed volume/filesystem boundary, and attempted privilege inflation.
- `SecureDeleteMutationLeaseManager` consumes only a coordinator authorization, revalidates it, opens the approved exact object with mutation-relevant access and restrictive sharing, verifies final path, reparse/directory/link state, and stable volume/file identity from the live handle, then retains that handle in `SecureDeleteMutationLease`.
- The retained lease blocks rename/replacement and new write-capable opens while it is active. The lease exposes no raw handle property and, at this milestone, exposes no delete, overwrite, truncate, or media operation.
- `SecureDeleteOperationJournal` persists the exact operation/authorization/target/storage binding and the destructive-state machine before any future irreversible action. Journal writes use a new temporary file, write-through I/O, explicit flush-to-disk, replacement of the durable record, and immediate readback verification.
- The journal enforces monotonic state transitions: `Prepared` -> `IdentityVerified` -> `PrimaryMutationStarted` -> `PrimaryRemovalVerified` -> `RelatedCleanupPending` -> `Complete`. `RecoveryRequired` may be entered from an unresolved nonterminal state and cannot silently transition to `Complete`.
- Journal reads fail closed on missing/invalid JSON, schema mismatch, mismatched operation identity, empty authorization/target/storage binding, undefined persisted enum states, invalid timestamps, or reversed update chronology.
- The journal never opens or mutates the approved target file. A future executor must successfully persist `PrimaryMutationStarted` before it may perform target mutation.
- Acceptance coverage proves ordinary and empty files, Unicode paths, invalid/device namespaces, explicit directory rejection, protected application location, system-critical filenames, hard links, target replacement, conservative unknown media semantics, authorization expiry, privilege-inflation rejection, preflight path-swap revocation, retained-handle identity binding, active-lease rename/write race blocking, path-swap rejection before lease acquisition, release of protections after disposal, journal reopen durability, exact target/authorization binding, state-skip rejection, terminal `RecoveryRequired`, undefined numeric-state tamper rejection, and byte-for-byte preservation of the approved target throughout journal operations.

Not implemented yet:

- no destructive Secure Delete executor
- no privileged Secure Delete broker mutation operation
- no overwrite/TRIM/deallocation action
- no related-copy cleanup action
- no destructive Explorer command

## Exact-target authorization

Before any destructive step Sentinel must:

1. canonicalize the user-selected path during selection/inspection
2. open/reopen the exact filesystem object
3. bind stable identity evidence from that object
4. verify the object remains the user-approved target
5. reject unexpected reparse behavior
6. reject unexpected hard-link/multiple-link conditions unless explicitly supported by policy
7. reject protected Windows locations
8. reject Program Files and Sentinel package/install locations
9. reject system-critical files and device namespaces
10. revalidate identity immediately before destructive mutation
11. **reopen and retain the exact verified object/handle through the destructive mutation itself**
12. persist `PrimaryMutationStarted` durably before the first irreversible target mutation

The current coordinator implements step 10, the qualified retained-handle lease implements the non-destructive acquisition/retention foundation for step 11, and the qualified operation journal implements the durable state prerequisite for step 12. A future executor must consume those exact authorities without falling back to path-only authority or reopening an unverified object later.

User intent, subscription state, path text, filename similarity, and prior inspection are not substitutes for exact-object validation.

## Broker / executor rule

No unrestricted delete API is permitted.

A future broker/executor request must contain a narrow operation identifier and sufficient expected identity evidence to independently reopen and verify the exact approved object. The executor must retain the verified object through mutation so another filesystem object cannot be substituted between authorization and deletion. It must fail closed if identity, link count, reparse state, protected-location policy, storage boundary, authorization, or durable journal state changes while UAC or IPC is pending.

The destructive executor must not expose a free-form `DeletePath(string)` or equivalent path-only primitive.

## Storage capability classification

`StorageCapabilityDetector` reports best-effort facts, not marketing promises:

- filesystem type
- local/removable/network/cloud-synchronized classification where detectable
- rotational media indication where Windows exposes it
- SATA/NVMe/other bus information where Windows exposes it
- BitLocker/protection state where available
- TRIM/unmap support indicators where safely queryable

Unknown information remains Unknown.

The current source foundation intentionally leaves physical-media, BitLocker, TRIM/unmap, and cloud-sync states Unknown rather than infer them from drive letters or filenames. More authoritative Windows/provider detection is a later source step and must include its own runtime fixtures.

## Media policy

### HDD / rotational media

Sentinel may support exact-file overwrite strategies only after exact-target handle-retention safeguards, sparse/compressed/encrypted-file behavior, allocation semantics, crash handling, and post-operation verification are reviewed. Overwrite success does not prove every historical sector/remapped sector is gone.

### SSD / NVMe / flash

Application-level overwrites do not prove physical NAND erasure because of wear leveling, controller remapping, over-provisioning, and device firmware behavior. Sentinel may overwrite logical content where safe and may request OS-supported deallocation/TRIM where applicable, but the result must distinguish the logical operation from physical certainty.

### BitLocker

Encryption-at-rest is relevant context but is not itself proof that a selected file's historical plaintext/ciphertext copies no longer exist.

### Cloud-synchronized paths

Local deletion does not prove remote-version deletion. Cloud copies are handled by discovery/provider-specific targeted cleanup when supported and are reported separately.

## Result model

### Primary file

- `VERIFIED` — Sentinel verified the approved filesystem object is no longer present at the exact target identity/path boundary it can prove.
- `FAILED` — the primary removal did not complete or could not be verified.

### Media action

- `VERIFIED` — a specific supported logical media action was completed and verified at the API level.
- `REQUESTED` — the OS/device accepted a request but Sentinel cannot prove lower-layer completion.
- `NOT SUPPORTED` — no safe supported targeted media action is available.

### Related copies

- `FOUND`
- `REMOVED`
- `REMAIN`

### External copies

- `FOUND`
- `UNKNOWN`
- `NOT INSPECTABLE`

### Physical media absence

- `CANNOT PROVE` unless exact platform/media evidence genuinely proves otherwise.

## Transaction model

Secure Delete uses a durable operation journal before the first destructive action.

Implemented states:

- Prepared
- IdentityVerified
- PrimaryMutationStarted
- PrimaryRemovalVerified
- RelatedCleanupPending
- Complete
- RecoveryRequired

Recovery never guesses. Ambiguous state remains actionable and visible.

The journal is a prerequisite, not a destructive executor. A future executor must make `PrimaryMutationStarted` durable before the first irreversible mutation and must not promote an ambiguous or tampered journal record to success.

## Cancellation and crash policy

Cancellation before destructive mutation: no change.

Cancellation after destructive mutation begins: finish only the minimum steps needed to establish and report safe/known state; never convert uncertainty into success.

Crash recovery must identify whether the primary target still exists, whether the exact original identity can still be established, and whether any related-artifact cleanup remains pending.

## Claims boundary

Allowed product phrasing:

- Secure Delete
- Maximum safe privacy removal
- Storage-aware secure deletion
- Verified removal where Sentinel can prove it

Prohibited without exact independent proof:

- forensically impossible to recover
- unrecoverable on every storage device
- military-grade deletion
- guaranteed NAND erase

## Required adversarial tests

- ordinary/empty/large file
- Unicode/long path
- read-only/locked/access denied
- symlink/junction/reparse
- hardlink
- target swap after confirmation
- target swap between preflight and mutation
- parent replacement
- protected Windows path
- Program Files
- Sentinel install/package path
- cloud sync path
- HDD/SATA SSD/NVMe/BitLocker
- cancellation
- crash at every transaction phase
- volume disappears
- disk/device errors
- duplicate discovery
- user declines
- false-positive duplicate resistance
- authorization expiry, replay, malformed privilege bits, and storage-boundary change
- malformed/tampered/undefined journal state and timestamp rollback

## Qualification state

**EXACT-TARGET FOUNDATION CI VERIFIED.** Exact-target binding/revalidation and conservative storage-capability reporting passed the active privacy harness, desktop build, native Explorer x64/x86/ARM64 builds, unsigned x64 MSIX build, and packaged x64 Explorer-extension PE verification at exact head `dd35c6e76a219840a9efeef0f441c9741b590a77`, workflow run `34726434160`.

**NON-DESTRUCTIVE COORDINATOR CI VERIFIED.** The short-lived exact-identity authorization/pre-mutation gate and its adversarial acceptance coverage passed the same complete workflow chain at exact head `42e88b2e6f1d9574eba344057d0db8b546393af3`, workflow run `34726779853`.

**RETAINED EXACT-HANDLE LEASE CI VERIFIED.** The mutation-time exact-object lease, including stable identity verification and active rename/write race blocking, passed the full privacy workflow at exact head `9c2630ccb41f519d1d6a8c60142023606c3534fa`, workflow run `34727592444`. The preceding run at `061ff227465c333dc942ca17a78c039731d24e42` exposed a harness-only sharing-semantics defect: the test attempted a competing read that did not share delete access while the retained lease was intentionally active. The test was corrected without changing product lease behavior.

**DURABLE OPERATION JOURNAL CI VERIFIED.** Exact operation/authorization/target binding, reopen durability, monotonic transitions, terminal recovery semantics, write-through/flush/readback persistence, target non-mutation, and undefined numeric-state tamper rejection passed the full privacy workflow at exact head `5e067877862a54e82de9cac17ef8885c09b737fa`, workflow run `34729111812`. The preceding journal run exposed two defects: the test fixture attempted to replace a symbolic enum name even though JSON stored the enum numerically (classification B), and production journal validation did not reject undefined numeric enum values (classification A). Production now validates persisted state and record invariants before accepting or re-persisting a record; the corrected test corrupts the numeric state to `999` and proves fail-closed rejection.

**DESTRUCTIVE EXECUTOR NOT IMPLEMENTED.** No Secure Delete broker mutation, retained-handle logical removal, overwrite, TRIM/deallocation, related-copy removal, or Explorer Secure Delete command exists yet. The next safe source milestone is recovery classification/reconciliation and/or a narrowly bound execution plan that consumes the qualified authorization, retained exact-object lease, and durable journal without falling back to path-only authority. Overwrite sanitization remains disabled until media-specific strategy is separately qualified.
