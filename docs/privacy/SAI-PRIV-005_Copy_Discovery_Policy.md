# SAI-PRIV-005 — Attributable Copy and History Discovery Policy

Status: DESIGN COMPLETE — IMPLEMENTATION PENDING  
Version: 1.0  
Date: 2026-09-12

## Purpose

Define how Sentinel searches broadly for copies, versions, historical entries, references, and Sentinel-created artifacts related to a selected file without turning fuzzy similarity into destructive authority.

Discovery and deletion are separate operations.

## Candidate classes

### CONFIRMED COPY
High-confidence evidence that the candidate is the same attributable content/object/version, for example:

- exact stable object identity where the provider exposes it
- exact content hash plus compatible size/type context
- provider-supplied exact version relationship
- Sentinel provenance record proving Sentinel created the artifact from the selected source

Eligible for targeted cleanup only after applicable policy/user confirmation and a fresh safety validation.

### LIKELY ATTRIBUTABLE COPY
Strong but incomplete attribution, such as reliable source metadata plus matching characteristics where an exact identity/hash is unavailable.

Normally requires explicit user confirmation. Never auto-delete merely because the filename matches.

### METADATA / REFERENCE ONLY
A reference to the selected item rather than a content copy, such as a Recent/Jump List/search/history reference.

Remove only through a documented provider-supported targeted path that does not erase unrelated history.

### UNVERIFIED CANDIDATE
Weak evidence, including fuzzy filename similarity alone.

Never delete automatically.

## Discovery sources

Implement as independent providers so unavailable sources do not silently become “nothing found.” Planned providers include:

- selected original filesystem object
- Sentinel temporary artifacts
- Sentinel investigation/history records
- File History where supported
- Previous Versions / shadow-copy references where safely inspectable
- application AutoRecover/temp locations only when attribution is strong
- Windows Search references
- Recent/Jump List references
- supported cloud synchronization providers
- content-hash-identical filesystem duplicates where bounded and appropriate

## Evidence rules

Use, in descending authority where available:

1. provider object/version identity
2. Sentinel provenance
3. exact cryptographic content hash
4. exact size + trusted provider relationship
5. bounded metadata correlation

Fuzzy name, extension, creation time, or directory proximity by themselves never authorize deletion.

Hashing must be bounded/cancelable and should not recursively hash an unlimited filesystem merely because one file was selected. Broad searches need explicit scope, progress, cancellation, and resource limits.

## Provider result contract

Each provider returns:

- provider name/version
- source searched
- availability state
- search start/end/result state
- candidates with classification
- evidence used
- whether targeted removal is supported
- limitations/errors

Discovery aggregation reports:

- sources searched
- sources unavailable
- confirmed copies
- likely copies
- metadata/reference-only items
- unresolved candidates
- removals performed
- items remaining
- limitations Sentinel cannot prove

## Deletion separation

Discovery results never directly call a privileged delete primitive.

Cleanup request flow:

`discovery result -> classification policy -> user/policy confirmation -> fresh provider/filesystem validation -> targeted cleanup -> post-verification`

A candidate that changes between discovery and cleanup loses its prior authorization and must be reclassified.

## Broad-history safety

If Windows/provider APIs only expose a broad destructive operation that would erase unrelated history, Sentinel must not run it silently for one selected file.

The UI must explain:

- what unrelated data would be affected
- what targeted cleanup is unavailable
- what remains after Sentinel's safe targeted actions

## Cloud providers

Local sync state is not proof of remote state. Provider integrations must distinguish:

- local copy
- synchronized remote object
- remote historical version
- unknown/not inspectable

Provider authentication tokens are never stored in Explorer handoff data and are not reusable entitlement secrets.

## Sentinel-owned artifacts

Sentinel-created temporary, investigation, export, recovery, or future privacy artifacts must carry provenance sufficient to relate them back to the source without relying on filename similarity.

Privacy-sensitive provenance should use stable random operation IDs and hashes rather than storing unnecessary plaintext source paths.

## False-positive resistance tests

At minimum:

- same filename/different content
- same size/different content
- renamed identical copy
- modified derivative
- duplicate with alternate metadata
- hardlink vs independent copy
- cloud placeholder vs hydrated copy
- stale search index entry
- stale Recent/Jump List reference
- File History version
- Previous Versions reference
- inaccessible provider
- provider timeout
- selected file changes during discovery
- candidate changes before cleanup

## Claims boundary

“Nothing found” is permitted only for the providers that completed successfully. Overall reporting must say which sources were unavailable or not inspectable.

Sentinel must never report “all copies removed” when any supported source failed, was unavailable, or returned unresolved candidates.

## Qualification state

DESIGN COMPLETE only. Provider implementations, resource bounds, targeted cleanup capabilities, cloud/provider behavior, and Windows runtime validation remain pending.
