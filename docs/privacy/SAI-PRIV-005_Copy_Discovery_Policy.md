# SAI-PRIV-005 — Attributable Copy and History Discovery Policy

Status: SOURCE IMPLEMENTED FOUNDATION / CI VERIFIED — PROVIDER + WINDOWS RUNTIME VALIDATION PENDING  
Version: 1.2  
Date: 2026-09-12

## Purpose

Define how Sentinel searches broadly for copies, versions, historical entries, references, and Sentinel-created artifacts related to a selected file without turning fuzzy similarity into destructive authority.

Discovery and deletion are separate operations.

## Candidate classes

### CONFIRMED COPY

High-confidence evidence that a candidate is independently attributable to the selected content, currently including exact SHA-256 content identity and/or strong Sentinel provenance combined with exact content identity. A hardlink/path resolving to the same filesystem object is not treated as an independent deletable copy.

A confirmed copy may be offered for targeted removal, but every filesystem removal receives fresh exact-target validation, fresh Secure Delete authorization, fresh Premium entitlement validation and its own journaled exact-object operation.

### LIKELY ATTRIBUTABLE COPY

Strong but incomplete provider attribution. Requires explicit user confirmation and fresh cleanup validation. Filename similarity never creates this classification by itself.

### METADATA_REFERENCE_ONLY

A reference rather than a content copy. Current source can classify reliable Windows Search / Recent / Jump List provider metadata supplied to the discovery session. Removal is permitted only through a safe provider-specific targeted operation; Sentinel must not translate metadata into arbitrary filesystem deletion or broad index/history destruction.

### UNVERIFIED_CANDIDATE

Weak or ambiguous evidence, including unreliable metadata and same-object/hardlink aliases. Never auto-delete.

## Source implementation

`RelatedArtifactDiscoveryService` implements a bounded, cancellation-aware provider aggregation foundation.

Current providers/capabilities:

1. **SentinelArtifacts** — caller-supplied stable Sentinel provenance records are correlated with exact SHA-256 content identity. Provenance without matching exact content does not become a confirmed copy.
2. **BoundedHashDuplicateSearch** — user/configured directory roots are searched without following reparse directories. Files are classified as confirmed only after exact SHA-256 equality.
3. **FileHistory** — configured safely inspectable File History roots can be searched read-only for exact-hash versions. If no safe root is available, provider state is `Unavailable`; Sentinel does not claim “nothing found.”
4. **PreviousVersions** — configured mounted/read-only snapshot roots can be searched for exact-hash copies. Sentinel does not delete an entire shadow-copy set.
5. **WindowsSearch** — reliable supplied provider references are classified as metadata/reference only. Weak references remain unverified.
6. **RecentJumpLists** — reliable supplied provider references are classified as metadata/reference only. Weak references remain unverified.
7. **OneDriveLocal** — supported local OneDrive sync roots can be searched for exact-hash copies. Local discovery is reported separately from remote provider state; current source does not claim remote deletion/history verification.

Unsupported/unavailable providers remain visible in results rather than being converted into a clean result.

## Evidence rules

Authority order remains:

1. exact provider object/version identity where available;
2. stable Sentinel provenance;
3. exact cryptographic content hash;
4. trusted provider relationship plus exact corroborating evidence;
5. bounded metadata correlation.

Same filename, extension, timestamp, size or directory proximity alone is insufficient for deletion authority. Size equality is used only as a hashing optimization in the bounded filesystem provider.

## Search bounds

The source contract explicitly bounds:

- total files considered;
- total bytes hashed;
- directory depth;
- elapsed duration;
- configured hashing concurrency;
- provider requests as provider integrations are added.

Default filesystem discovery bounds are conservative and configurable through `RelatedArtifactDiscoveryBounds`. Cancellation is honored. Reparse directories are not traversed. Provider results report `Completed`, `Limited`, `Unavailable`, `Failed` or `Canceled`.

The Sentinel-provenance provider now reserves the same global hashed-byte budget before reading candidate content; it cannot bypass the aggregate hash-byte limit. If a bound is reached, Sentinel returns partial/limited results honestly.

## Provider result contract

Each provider reports:

- provider name;
- source searched;
- availability/result state;
- classified candidates;
- evidence used;
- whether targeted removal is supported;
- files scanned / bytes hashed where applicable;
- limitations/errors.

Aggregation reports confirmed/likely/metadata/unverified candidates and unavailable/limited providers. “Nothing found” applies only to providers that actually completed.

## Related cleanup authority

`RelatedArtifactCleanupService` is separate from discovery.

Filesystem cleanup flow:

`discovery result -> classification -> explicit confirmation -> fresh SecureDeleteTargetValidator -> fresh SecureDeleteCoordinator authorization -> fresh server-authoritative privacy.secure-delete capability -> independent SecureDeleteExactObjectExecutor operation -> post-verification`

Rules:

- the primary file authorization is never reused;
- confirmed filesystem copies require explicit confirmation in the current implementation;
- likely attributable copies require explicit confirmation;
- metadata references require a provider-specific targeted cleanup implementation;
- unverified candidates are rejected;
- hardlink/same-filesystem-object aliases are rejected as independent related-copy targets;
- any candidate that changes before cleanup loses its discovery-time relevance and must pass fresh exact-target validation.

The primary Secure Delete journal remains `RelatedCleanupPending` until the application has explicitly resolved the related-artifact phase. `CompleteRelatedPhase` performs no deletion; it only records that resolution after the primary operation is already verified.

## Broad-history safety

Sentinel does not silently run broad destructive provider operations for a single selected file. In particular, source policy forbids using related cleanup to delete entire:

- File History stores;
- Previous Versions / shadow-copy sets;
- Windows Search indexes;
- restore/history stores.

`pagefile.sys`, `swapfile.sys` and `hiberfil.sys` are not searched for selected-file content.

## Cloud providers

Local synchronization state is not proof of remote state. OneDrive source support is currently local-root discovery only. Remote object/history state remains unverified unless a future provider integration returns reliable API evidence. Sentinel must not claim remote deletion merely because a local copy disappeared.

Provider authentication data must never be stored in Explorer handoff records.

## Sentinel-owned artifacts

Sentinel artifacts use source hash plus stable operation/provenance data. Exact provenance is stronger than filenames but still does not bypass content verification or cleanup safety. The discovery API does not itself delete Sentinel artifacts.

## Deterministic/adversarial coverage

Current acceptance coverage includes:

- renamed exact-hash duplicate;
- same filename / different content rejection;
- same size / different content rejection;
- Sentinel provenance + hash;
- Sentinel-provenance hashed-byte bound enforcement;
- File History configured-root result;
- Previous Versions configured-root result;
- reliable metadata-only reference;
- weak metadata unverified classification;
- OneDrive local result with remote state unverified;
- hardlink/same-object independent-delete rejection;
- bounded search;
- cancellation;
- unavailable-provider reporting.

Still required in installed/provider validation includes cloud placeholders/hydration, real File History APIs/layouts, real Previous Versions/shadow snapshots, stale Windows Search/Jump List entries, provider timeout/error behavior, candidate changes during cleanup, OneDrive remote metadata/history where a reliable supported API is introduced, and full resource/performance validation.

## Claims boundary

Sentinel must never say “all copies removed” when any relevant provider failed, was unavailable/limited, remote state is unverified, or unresolved candidates remain.

Current UI/result wording must separate:

- primary exact-object removal;
- local related candidates;
- metadata references;
- provider sources not inspected;
- remote/cloud state;
- physical-media certainty.

## Qualification state

- **DESIGN:** complete for the current provider model.
- **SOURCE IMPLEMENTED:** yes for the provider/cleanup foundation above.
- **CI VERIFIED:** **YES** at source checkpoint `75edc968d243ccda8da84ea05d687d881270f28f`, workflow run `34734415429` (**SUCCESS**).
- **STAGING VERIFIED:** not applicable to local providers; cloud remote-provider support is not claimed.
- **WINDOWS RUNTIME REQUIRED:** yes.
- **FULLY QUALIFIED:** no.

See `SAI-PRIV-007_PreWindows_Adversarial_Review.md` for the separate adversarial review and remaining external/runtime gates.
