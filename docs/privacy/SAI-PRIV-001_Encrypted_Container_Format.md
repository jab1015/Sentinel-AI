# SAI-PRIV-001 — Encrypted Container Format

Status: DESIGN COMPLETE — SOURCE IMPLEMENTATION IN PROGRESS  
Version: 1.1  
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
| HeaderLength | 4 bytes | Exact byte count from the first byte of `Magic` through the final wrapped-key-record byte; excludes the 16-byte `HeaderTag` |
| AlgorithmId | 2 bytes | `1` = AES-256-GCM |
| ChunkSize | 4 bytes | Plaintext bytes per full data chunk; v1 default 1 MiB |
| OriginalLength | 8 bytes | Exact plaintext length |
| ChunkCount | 8 bytes | Exact number of data chunks |
| NoncePrefix | 8 bytes | Random per-container nonce prefix |
| KeyRecordCount | 2 bytes | Number of wrapped-key records |
| Flags | 4 bytes | Reserved; unknown required flags fail closed |

The fixed preamble is exactly 50 bytes. `HeaderLength` includes those 50 bytes plus all serialized wrapped-key records, and excludes `HeaderTag`. This makes the authenticated header boundary self-delimiting without relying on provider-specific key-record parsing. Version 1 rejects `HeaderLength < 50`, headers larger than 256 KiB, lengths beyond the physical file, and any key-record parse that does not end exactly at `HeaderLength`.

### Wrapped-key records

Each key record is serialized in this exact order:

| Field | Size |
|---|---:|
| RecordVersion | 2 bytes |
| ProtectionModeId | 2 bytes |
| WrappingAlgorithmId | 2 bytes |
| ParameterLength | 4 bytes |
| ParameterBytes | ParameterLength bytes |
| WrappedDekLength | 4 bytes |
| WrappedDekBytes | WrappedDekLength bytes |

Version 1 protection modes are reserved as:

- `1` — Windows current-user protection
- `2` — password-derived wrapping key
- `3` — independent recovery key
- `4` — Sentinel Vault item key wrapping

The content DEK is independent of every wrapping mode. Adding or removing a wrapping mode does not re-encrypt file content.

### Header authentication

The fixed preamble and all wrapped-key records are authenticated with AES-256-GCM using the file DEK, empty plaintext, and a dedicated header nonce:

- nonce = `NoncePrefix || 0xFFFFFFFF`
- AAD = the exact `HeaderLength` bytes from `Magic` through the final wrapped-key-record byte
- `HeaderTag` = 16 bytes immediately following those bytes

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

Chunk AAD is exactly 48 bytes:

- bytes 0–31: SHA-256 of `HeaderBytes || HeaderTag`
- bytes 32–35: chunk index as UInt32 big-endian
- bytes 36–39: plaintext chunk length as Int32 little-endian
- bytes 40–47: original plaintext length as Int64 little-endian

The same file DEK is used only with unique nonces inside that container. Version 1 permits at most `UInt32.MaxValue` data chunks, which means the greatest valid data chunk index is `UInt32.MaxValue - 1`; `0xFFFFFFFF` remains reserved exclusively for header authentication.

## Streaming rules

- Read source through a bounded buffer no larger than the configured chunk size.
- Encrypt one chunk at a time.
- Write exactly one record per expected chunk.
- Flush the encrypted output before verification.
- Reopen the encrypted output from disk.
- Parse bounded header/key-record lengths before proportional allocation.
- Obtain the DEK through an authenticated key-protection record.
- Authenticate the header before any plaintext result is accepted.
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

A failed/canceled encryption may remove its own incomplete output. If cleanup of incomplete output fails, the structured result must explicitly report that an invalid partial artifact remains; it must never describe that artifact as an encrypted copy.

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
- wrapped-key authentication failure
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

DESIGN COMPLETE. P3 source implementation is now authorized against this v1.1 serialization contract. Source implementation, corruption/adversarial harnesses, Windows runtime tests, crash/disk-full tests, key-mode tests, and independent cryptographic review remain required before release qualification.
