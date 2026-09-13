# SAI-PRIV-004 — Secure Delete Design

Status: EXACT-OBJECT LOGICAL REMOVAL SOURCE IMPLEMENTED / CI VERIFIED — STAGING ENTITLEMENT + WINDOWS MEDIA VALIDATION PENDING  
Version: 1.8  
Date: 2026-09-12

## Purpose

Define a storage-aware exact-target removal architecture that maximizes safe privacy removal without damaging unrelated Windows/user data and without claiming physical certainty the platform cannot prove.

## Non-goals

Secure Delete is not:

- a whole-drive sanitize command;
- firmware secure erase;
- an unrestricted privileged `DeletePath(string path)` / arbitrary file-delete API;
- permission to erase unrelated restore/history/search data;
- permission to scan or manipulate `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys` for selected-file content;
- proof that SSD/NVMe/NAND physical cells no longer contain historical data.

## Implemented components

- `SecureDeleteTargetValidator`
- `StorageCapabilityDetector`
- `SecureDeleteCoordinator`
- `SecureDeleteMutationLeaseManager` / `SecureDeleteMutationLease`
- `SecureDeleteOperationJournal`
- `SecureDeleteRecoveryClassifier`
- `SecureDeleteExactObjectExecutor`
- `RelatedArtifactDiscoveryService`
- `RelatedArtifactCleanupService`

## Exact-object safety boundary

Before destructive mutation Sentinel requires:

1. a previously validated `SecureDeleteTargetIdentity`;
2. nonexpired short-lived `SecureDeleteAuthorization`;
3. exact target revalidation;
4. storage-boundary revalidation;
5. retained handle acquisition with mutation-relevant access and restrictive sharing;
6. final handle-derived canonical path equality;
7. no reparse point;
8. not a directory;
9. exactly one hard link in the first destructive release;
10. stable volume/file identity equal to the approved identity;
11. protected/system-critical/Program Files/Sentinel/package/device-namespace policy still satisfied through the validator/coordinator boundary;
12. server-authoritative `privacy.secure-delete` entitlement immediately before the local destructive operation in app flows.

If the retained handle cannot prove the approved exact identity, mutation fails closed.

No executor method accepts an arbitrary pathname for deletion. The only destructive primitive added to the retained lease is `TryRequestLogicalRemoval`, which invokes Windows file-disposition semantics on the already-open, already-verified `SafeFileHandle`.

## Durable one-use authorization

`SecureDeleteOperationJournal.Begin` durably claims each `AuthorizationId` with a create-new write-through claim record before persisting `Prepared`. A second begin with the same authorization is rejected, including after a process restart.

The claim is intentionally retained even if later operation persistence fails. Requiring a fresh user approval is safer than attempting to recycle destructive authority after ambiguous startup/storage failure.

This claim complements, rather than replaces, authorization expiration, target/storage revalidation, retained-handle identity verification and authenticated journal state.

## Destructive executor transaction

`SecureDeleteExactObjectExecutor` performs the first destructive version as follows:

1. coordinator revalidates authorization, expiry, exact identity and storage boundary;
2. mutation lease manager revalidates again and retains the exact object handle;
3. executor verifies authorization/target/storage facts match the retained lease;
4. journal creates `Prepared` under a one-use authorization claim;
5. journal advances to `IdentityVerified` and the record is read back/authenticated;
6. journal advances to `PrimaryMutationStarted` and the record is read back/authenticated before irreversible removal;
7. the retained handle is marked delete-pending through `SetFileInformationByHandle`;
8. the retained handle closes, allowing Windows to complete logical exact-object removal;
9. Sentinel observes the original target identity/path boundary;
10. if the original stable identity still exists, result is failed;
11. if evidence is ambiguous, journal becomes/remains `RecoveryRequired` and primary status is `UNKNOWN`;
12. if the pathname now contains a different replacement object, Sentinel records that the original identity is absent and leaves the replacement untouched;
13. journal advances to `PrimaryRemovalVerified`;
14. journal advances to `RelatedCleanupPending`;
15. executor returns structured status and does not automatically mark `Complete`.

## Journal and recovery

The journal remains authenticated using an exact-record SHA-256 digest wrapped by the qualified Windows current-user key protector. Records use write-through/flush/readback persistence and monotonic transitions:

`Prepared -> IdentityVerified -> PrimaryMutationStarted -> PrimaryRemovalVerified -> RelatedCleanupPending -> Complete`

`RecoveryRequired` may be entered from unresolved nonterminal states and cannot silently transition to `Complete`. Missing, malformed, tampered, undefined-state, reversed-time, mismatched-operation, target/storage/authorization-binding, or integrity-proof failures fail closed.

`SecureDeleteRecoveryClassifier` never automatically resumes destructive work. Before durable `PrimaryMutationStarted`, a still-identical target may only lead to a fresh authorization. After `PrimaryMutationStarted`, ambiguous or contradictory evidence requires recovery review. A different replacement object at the old pathname is outside the old authority. Related cleanup is never automatically resumed as broad deletion.

Deterministic journal/recovery acceptance covers prepared/mutation states, tampering, path reuse, contradictory live evidence, missing/replaced targets, terminal recovery and authorization replay. The destructive executor harness covers normal/empty/Unicode removal, read-only fail-closed behavior, expiry, target replacement, durable `RelatedCleanupPending`, and honest media semantics.

Full injected-failure/device coverage through every destructive timing window remains a required Windows/runtime qualification item and must not be represented as physically qualified by source tests alone.

## Related artifacts

Primary removal never grants authority over another object. Discovery is read-only classification.

`RelatedArtifactCleanupService` requires for every filesystem candidate an eligible class, explicit confirmation in the current implementation, fresh exact-target validation, fresh Secure Delete authorization, fresh `privacy.secure-delete` entitlement, and a separate journal/executor operation. Unverified candidates and same-object/hardlink aliases are rejected. Metadata references require provider-specific targeted operations. The primary journal may reach `Complete` only after the related phase is explicitly resolved.

## Media behavior

The first destructive release implements logical exact-object removal only.

Primary removal result values are `VERIFIED`, `FAILED`, or `UNKNOWN`. Media action values are `VERIFIED`, `REQUESTED`, `NOT_SUPPORTED`, or `CANNOT_PROVE`.

The current executor reports media overwrite/TRIM/deallocation as `NOT_SUPPORTED`. Physical-media absence is `CANNOT_PROVE` unless future independent platform/media evidence genuinely proves otherwise. Application overwrites or TRIM acceptance must never be marketed as proof of SSD/NVMe/NAND physical erasure.

## Storage capability policy

`StorageCapabilityDetector` remains conservative. Unknown physical media, BitLocker, TRIM/unmap and cloud-sync facts remain `Unknown` rather than inferred from drive letters or filenames. First destructive execution is limited to validated local fixed storage. Future media-specific behavior requires separate review and real HDD/SATA SSD/NVMe/BitLocker validation.

## Premium entitlement boundary

Entitlement does not create filesystem authority. A paid user cannot override target protection, reparse/link restrictions, exact identity or storage safety. Application destructive flows obtain a one-operation `privacy.secure-delete` capability through the existing Store-authoritative gateway and validate it immediately before local execution. Store/gateway unavailability blocks new premium mutation; it does not lock already-owned encrypted data.

See `SAI-PRIV-006_Premium_Entitlement.md`.

## Explorer boundary

The native Explorer extension performs no deletion, cryptography, entitlement validation, scanning, cloud access, broker access or secret handling. It sends one-time action intent and selection to Sentinel. Sentinel reopens the object, displays confirmation and performs authoritative checks.

## Claims boundary

Allowed with appropriate context/evidence: Secure Delete; logical exact-object removal verified; storage-aware privacy removal; verified removal where Sentinel can prove it.

Prohibited without exact independent proof: “100% unrecoverable,” “forensically impossible to recover,” “guaranteed NAND erase,” “guaranteed physical-media absence,” or equivalent claims.

## Required remaining qualification

Windows validation must include large/long-path/locked/access-denied files; symlink/junction/reparse and parent-replacement races; hardlinks/protected Windows/Program Files/Sentinel package targets; concurrency; cancellation/failure around destructive timing windows; journal loss/tamper/rollback; path reuse/replacement; volume loss/device errors; real HDD/SATA SSD/NVMe/BitLocker; install/package/UAC; and related-provider behavior.

## Qualification state

- **DESIGN:** complete for the first logical-removal version.
- **SOURCE IMPLEMENTED:** yes for exact-object logical removal and separate related cleanup foundation.
- **CI VERIFIED:** **YES** at source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f`, workflow run `34734415429` (**SUCCESS**).
- **STAGING VERIFIED:** no; entitlement staging is blocked on shared multi-instance gateway state.
- **WINDOWS RUNTIME REQUIRED:** yes.
- **FULLY QUALIFIED:** no.

See `SAI-PRIV-007_PreWindows_Adversarial_Review.md` for the separate adversarial source review.
