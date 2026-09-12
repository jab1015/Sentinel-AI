# SAI-PRIV-001 — Encrypted Container Format

Status: DESIGN COMPLETE — SOURCE IMPLEMENTATION NOT YET QUALIFIED  
Version: 1.0  
Date: 2026-09-12

## Purpose

Define the Sentinel encrypted-file container before encryption code is implemented. The format is versioned, authenticated, streaming-friendly, and designed so a failed encryption or verification never destroys the source file.

## Security goals

- AES-256-GCM only for version 1 content encryption.
- Fresh cryptographically random 256-bit data-encryption key (DEK) for every encrypted file.
- No raw DEK stored in the container.
- No AES-GCM key/nonce pair is ever reused.
- Header and key metadata are authenticated before any plaintext result is accepted.
- Large files are processed in bounded chunks; the whole file is never required in memory.
- Truncation, reordering, corruption, duplicate chunks, missing chunks, and trailing data fail closed.
- Unknown critical fields or unsupported versions fail closed.

## File layout

All integers are little-endian unless explicitly stated.

### Fixed preamble

| Field | Size | Meaning |
|---|---:|---|
| Magic | 8 bytes | ASCII `SNTLENC1` |
| FormatVersion | 2 bytes | `1` |
| HeaderLength | 4 bytes | Total authenticated header bytes following the fixed preamble |
| AlgorithmId | 2 bytes | `1` = AES-256-GCM |
| ChunkSize | 4 bytes | Plaintext bytes per full data chunk; v1 default 1 MiB |
| OriginalLength | 8 bytes | Exact plaintext length |
| ChunkCount | 8 bytes | Exact number of data chunks |
| NoncePrefix | 8 bytes | Random per-container nonce prefix |
| KeyRecordCount | 2 bytes | Number of wrapped-key records |
| Flags | 4 bytes | Reserved; unknown required flags fail closed |

`HeaderLength` is bounded. Version 1 rejects headers larger than 256 KiB.

### Wrapped-key records

Each key record contains:

- record version
- protection mode identifier
- wrapping/KDF algorithm identifier
- bounded parameter length
- bounded parameter bytes
- bounded wrapped-DEK length
- wrapped-DEK bytes

Version 1 protection modes are reserved as:

- `1` — Windows current-user protection
- `2` — password-derived wrapping key
- `3` — independent recovery key
- `4` — Sentinel Vault item key wrapping

The content DEK is independent of every wrapping mode. Adding or removing a wrapping mode does not re-encrypt file content.

### Header authentication

The fixed preamble and all wrapped-key records are authenticated with AES-256-GCM using the file DEK, empty plaintext, and a dedicated header nonce:

- nonce = `NoncePrefix || 0xFFFFFFFF`
- AAD = every header byte before `HeaderTag`
- `HeaderTag` = 16 bytes

The reserved header nonce is never used for a data chunk.

### Data chunk records

For chunk index `i`, where `0 <= i < ChunkCount`:

| Field | Size |
|---|---:|
| ChunkIndex | 4 bytes |
| PlaintextLength | 4 bytes |
| Ciphertext | PlaintextLength bytes |
| AuthenticationTag | 16 bytes |

Data nonce:

`NoncePrefix || UInt32BigEndian(i)`

Chunk AAD is:

- SHA-256 of the authenticated header bytes including `HeaderTag`
- chunk index
- plaintext length
- original plaintext length

The same file DEK is used only with unique nonces inside that container. Version 1 therefore limits data chunks to at most `UInt32.MaxValue` and rejects configurations that would exceed that limit.

## Streaming rules

- Read source through a bounded buffer no larger than the configured chunk size.
- Encrypt one chunk at a time.
- Write exactly one record per expected chunk.
- Flush the encrypted output before verification.
- Reopen the encrypted output from disk.
- Authenticate the header.
- Authenticate every chunk in order.
- Verify exact chunk count, exact original length, and EOF immediately after the final tag.
- Extra trailing bytes are corruption and fail closed.

## Source transaction

The initial implementation must never encrypt in place.

`SOURCE -> validate -> create separate output -> encrypt -> flush -> reopen -> authenticate -> verify -> report verified encrypted copy`

Only after this sequence may a separate plaintext-removal operation be offered.

If encryption fails: keep the original.  
If verification fails: keep the original and treat output as invalid.  
If optional plaintext removal later fails: keep the verified encrypted copy and report that plaintext remains.

## Size limits

Version 1 defaults:

- chunk size: 1 MiB
- minimum chunk size: 64 KiB
- maximum chunk size: 8 MiB
- maximum authenticated header: 256 KiB
- maximum key records: 16
- maximum individual key-record parameter section: 64 KiB
- maximum wrapped-DEK blob: 64 KiB

Values outside the supported range are rejected before allocating proportional buffers.

## Metadata

Sensitive original-path metadata is not required in v1 and must not be stored by default. If future versions add display metadata, it must be authenticated and should be encrypted where practical.

## Corruption behavior

Any of the following produces a structured verification failure and no plaintext success result:

- bad magic
- unsupported version/algorithm
- impossible lengths/counts
- header authentication failure
- unknown required flags
- unsupported key record
- duplicate/invalid key record structure
- wrong chunk index
- invalid chunk length
- tag failure
- truncation
- trailing data
- integer overflow

## Compatibility

Readers must reject newer unsupported major format versions. Minor-compatible extensions, if later introduced, must be explicitly marked optional. Version 1 writers emit only version 1 records.

## Logging rules

Never log:

- DEKs
- wrapping keys
- recovery keys
- passwords
- password-derived keys
- authentication tags when they can assist correlation of sensitive artifacts

Logs may contain bounded operation IDs, result codes, byte counts, algorithm/version identifiers, and sanitized destination names when allowed by the diagnostics policy.

## Qualification state

DESIGN COMPLETE only. This document does not make encryption production-ready. Source implementation, corruption/adversarial harnesses, Windows runtime tests, crash/disk-full tests, key-mode tests, and independent cryptographic review remain required.
