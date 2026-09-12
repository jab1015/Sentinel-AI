# SAI-PRIV-004 — Secure Delete Design

Status: DESIGN COMPLETE — DESTRUCTIVE IMPLEMENTATION NOT STARTED  
Version: 1.0  
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

- `SecureDeleteCoordinator`
- `StorageCapabilityDetector`
- `SecureDeleteResult`
- narrow privileged broker operation bound to exact object identity
- `RelatedArtifactDiscoveryService`

## Exact-target authorization

Before any destructive step Sentinel must:

1. canonicalize the user-selected path
2. open/reopen the exact filesystem object
3. bind stable identity evidence from that object
4. verify the object remains the user-approved target
5. reject unexpected reparse behavior
6. reject unexpected hard-link/multiple-link conditions unless explicitly supported by policy
7. reject protected Windows locations
8. reject Program Files and Sentinel package/install locations
9. reject system-critical files and device namespaces
10. revalidate identity immediately before destructive mutation

User intent, subscription state, path text, filename similarity, and prior inspection are not substitutes for exact-object validation.

## Broker rule

No unrestricted delete API is permitted.

A future broker request must contain a narrow operation identifier and sufficient expected identity evidence for the broker to independently reopen and verify the exact approved object before mutation. The broker must fail closed if the object changed while UAC was pending.

## Storage capability classification

`StorageCapabilityDetector` reports best-effort facts, not marketing promises:

- filesystem type
- local/removable/network/cloud-synchronized classification where detectable
- rotational media indication where Windows exposes it
- SATA/NVMe/other bus information where Windows exposes it
- BitLocker/protection state where available
- TRIM/unmap support indicators where safely queryable

Unknown information remains Unknown.

## Media policy

### HDD / rotational media

Sentinel may support exact-file overwrite strategies only after exact-target safeguards, sparse/compressed/encrypted-file behavior, allocation semantics, and crash handling are reviewed. Overwrite success does not prove every historical sector/remapped sector is gone.

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

Secure Delete must use durable transaction state before the first destructive action.

Minimum states:

- Prepared
- IdentityVerified
- PrimaryMutationStarted
- PrimaryRemovalVerified
- RelatedCleanupPending
- Complete
- RecoveryRequired

Recovery never guesses. Ambiguous state remains actionable and visible.

## Cancellation and crash policy

Cancellation before destructive mutation: no change.

Cancellation after destructive mutation begins: finish only the minimum steps needed to establish and report safe/known state; never convert uncertainty into success.

Crash recovery must identify whether the primary target still exists and whether any related-artifact cleanup remains pending.

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

## Qualification state

DESIGN COMPLETE only. No destructive Secure Delete broker operation exists yet, by design. Exact-target implementation begins only after this design is reviewed against the hardened quarantine/broker patterns and must ship with adversarial tests from its first source commit.
