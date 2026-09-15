# SAI-PRIV-007 — Pre-Windows Premium Privacy Adversarial Review

Status: SOURCE REVIEW COMPLETE / CI VERIFIED / WINDOWS RUNTIME PHASE ACTIVE / STAGING BLOCKERS OPEN  
Date: 2026-09-12

## Reviewed checkpoint

- Branch: `feature/premium-privacy-foundation`
- Source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`
- Premium Privacy workflow run: `34734415429`
- Conclusion: **SUCCESS**
- Hardening branch was not modified or merged.

## Phase decision

The reviewed checkpoint is **ready to enter installed Windows physical validation**.

The open Google Cloud multi-instance shared-state issue and missing real Store test-entitlement evidence remain external/staging blockers for release qualification, but they do **not** prevent Windows runtime testing of Explorer, encryption, Vault, Secure Delete, discovery, local safety boundaries, crash/recovery behavior, filesystem/provider behavior, or installed app flows. Windows testing and GCP/Store external qualification should proceed in parallel.

This does not authorize production release or merge to `main`.

## Review scope

The review challenged the new Premium Privacy paths for arbitrary/path-only deletion, TOCTOU, reparse escape, hardlink abuse, protected-path bypass, stale/replayed authorization, entitlement bypass, capability replay, same-user Explorer handoff abuse, journal tampering/recovery ambiguity, replacement-object deletion, broad history deletion, duplicate false positives, cloud deletion overclaim, plaintext/key leakage, and Secure Delete marketing overclaim.

## Findings corrected during this work

### PP-AR-001 — Medium — Secure Delete authorization replay

**Area:** `SecureDeleteOperationJournal.cs`  
**Failure mode:** a previously issued destructive authorization did not initially have a durable one-use claim across restart/concurrent begin attempts.  
**Correction:** each `AuthorizationId` is now claimed with create-new/write-through durable state before `Prepared`; reuse is rejected and requires fresh approval.  
**Test:** `SecureDeleteAuthorizationReplayAcceptance` verifies restart/concurrent replay rejection.

### PP-AR-002 — Medium — Sentinel artifact hashing could bypass the global byte budget

**Area:** `RelatedArtifactDiscoveryService.cs`  
**Failure mode:** the Sentinel-provenance provider reserved file-count budget but did not reserve hashed-byte budget before reading candidate content. A sufficiently large provenance set could exceed the configured global hash-byte limit.  
**Correction:** Sentinel artifact hashing now reserves the byte budget before the first candidate read; the provider returns `Limited` without hashing when the budget is exhausted.  
**Test:** the discovery harness requires the Sentinel provider to report a byte-limit result with zero candidate bytes hashed when the source hash has already consumed the configured budget.

### PP-AR-003 — Low/build — Explorer submenu native portability regression

**Area:** `SentinelExplorerCommand.cpp` / `Sentinel.ExplorerExtension.vcxproj`  
**Failure mode:** the submenu enumerator used unqualified `min` while the project correctly defines `NOMINMAX`, causing x64 native compilation to fail.  
**Correction:** a forced compatibility header imports `std::min`; no Explorer trust-boundary behavior was weakened.  
**Evidence:** x64, x86 and ARM64 native Explorer builds all pass at the reviewed checkpoint.

## Open external blocker

### PP-AR-004 — Medium/staging — replay/rate/concurrency state is process-local

**Area:** `PrivacyCapabilitySecurity.cs`, gateway rate limiting/provider concurrency  
**Failure mode:** one-time privacy capability consumption and existing request/provider limits are maintained in process memory. A multi-instance Cloud Run deployment cannot prove cross-instance one-time replay rejection or global rate/concurrency enforcement.  
**Required correction:** move the relevant one-time/replay and limit state to a shared atomic backend appropriate for multi-instance Cloud Run, then test concurrent consumption, restart, failover and rate behavior across at least two instances.  
**Release effect:** GCP staging may not be marked VERIFIED until corrected and tested. This remains a release/staging blocker, but not a blocker to installed Windows physical validation.

## Trust-boundary review results

- **Arbitrary/path-only deletion:** no generic privileged delete API was introduced. Primary mutation is performed only through a retained verified object handle.
- **TOCTOU / replacement:** exact identity is revalidated before lease acquisition; the retained handle remains bound through mutation; a replacement object at the old path is outside prior authority.
- **Reparse/directory/hardlink/protected paths:** target validator/coordinator controls remain mandatory and are not bypassed by entitlement.
- **Stale authorization:** expiry plus durable one-use claim fail closed.
- **Entitlement bypass:** Premium Privacy creation/cleanup flows obtain server-authoritative Store-backed one-shot capabilities immediately before the operation. Explorer carries no entitlement authority.
- **Same-user Explorer handoff:** handoff records contain bounded action intent/paths only; Sentinel reopens the filesystem object and performs confirmation, entitlement and feature safety checks.
- **Journal tampering/recovery ambiguity:** authenticated records and read-only recovery classification fail closed; mutation is never automatically resumed after ambiguous destructive state.
- **Broad history deletion:** discovery exposes no whole-history/search/shadow-store delete primitive. Related filesystem candidates require independent exact-target authorization.
- **False positives:** same name/extension/time/size do not create deletion authority; exact content/provenance evidence is required for confirmed copies.
- **Cloud overclaim:** OneDrive support is local-root discovery only; remote history/deletion is not claimed.
- **Plaintext/key leakage:** Explorer handoffs carry no keys/secrets; Vault plaintext master keys are not persisted; recovery material is user-controlled and not silently escrowed.
- **Marketing overclaim:** logical removal is separated from overwrite/TRIM/physical-media claims; physical-media absence remains `CANNOT PROVE` unless independently demonstrated.

## Qualification conclusion

Confidence in the **source/automated Premium Privacy checkpoint**: **0.94**. The reviewed source is suitable for installed Windows physical validation, but it is not production qualified.

Confidence that **GCP staging is not yet qualified**: **0.99**, because shared multi-instance replay/rate/concurrency enforcement and real Store/Partner Center evidence remain unproven.

## Windows validation focus

Begin Windows testing with:

1. installed Explorer integration and package lifecycle;
2. encryption and recovery failure matrices;
3. Vault lifecycle, session locking, crash/corruption and recovery;
4. Secure Delete exact-object mutation, reparse/link/path-swap/protected-target/UAC/crash-state cases;
5. real File History, Previous Versions, Windows Search, Recent/Jump List, OneDrive local sync and duplicate-discovery behavior;
6. standard-user/admin and architecture-specific runtime coverage;
7. entitlement-driven installed flows where real Store test states are available.

Any runtime defect must be fixed at root cause, receive regression coverage, and be requalified in CI.

## Parallel external gates

Continue in parallel:

1. shared multi-instance gateway replay/rate/concurrency state;
2. staging-only GCP deployment with Secret Manager, least-privilege IAM, safe logging, alerting, restart/failover and budget controls;
3. real Microsoft Store test-entitlement evidence for active/inactive/expired/revoked/unavailable/network-unavailable cases;
4. signed package/Store provenance qualification.

## Remaining final gates

After Windows and external qualification:

1. final signed/package architecture runtime qualification;
2. fresh 1-hour and 8-hour stability/resource runs;
3. independent final privacy/security review;
4. High-finding adversarial re-audit;
5. full 29-finding re-audit and new-vulnerability review.

No merge to hardening or `main` is authorized by this review.
