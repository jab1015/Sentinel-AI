# SAI-005 — Product Roadmap

Version: 2.9  
Status: Active — Hardening physical qualification + isolated Premium Privacy implementation  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Branch Isolation Rule

- Hardening qualification branch: `security/production-hardening-1218f5d`
- Hardening source/test qualified checkpoint: `cff46692d1260349eae531632170fb687deed36f`
- Premium Privacy branch: `feature/premium-privacy-foundation`
- Premium Privacy implementation checkpoint before this documentation synchronization: `16e2dc91cbe71d3ca96275d89cf333ba40356236`

Premium Privacy remains isolated from the hardening qualification branch. Neither branch may be merged automatically to `main`.

## Phase A — Production Security Hardening

Status: **SOURCE/AUTOMATED QUALIFIED — PHYSICAL/EXTERNAL VALIDATION ACTIVE**

All 12 required hardening workflows are green at exact checkpoint `cff46692d1260349eae531632170fb687deed36f`.

Remaining hardening work:

1. Installed broker/UAC adversarial testing.
2. Authenticode runtime fixtures.
3. Installed quarantine/crash/recovery matrix.
4. Defender/firewall, driver/device, temp cleanup, service restart, Ask Sentinel UI, network churn, persistence, startup/lifecycle, install/update/uninstall, sleep/wake, failure matrices.
5. A05 Google Cloud + Store entitlement staging.
6. Signed Store package and real x86/x64/ARM64 runtime qualification.
7. Fresh final 1-hour and 8-hour stability/resource runs.
8. Final High-finding and full 29-finding adversarial re-audit.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT YET RELEASE-QUALIFIED**

Objectives remain malicious/suspicious behavior detection, honest-confidence ransomware/malware indicators, user warning/explanation, supported blocking/containment, quarantine/restore, network containment, Defender integration, and exact-target elevated remediation.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — RUNTIME VALIDATION REMAINS**

Final displayed answers are revalidated and cannot claim security actions without verified action evidence.

## Phase D — Cloud and Entitlement Validation

Status: **BLOCKED ON EXTERNAL VALIDATION**

Google Cloud staging, IAM, Secret Manager, distributed replay/rate state, Store entitlement lifecycle, abuse/load/outage/restart behavior, alerts/logging and spend controls remain required.

## Phase E — Release Qualification

Status: **ACTIVE AFTER SOURCE QUALIFICATION**

Signed package, install/upgrade/uninstall, supported Windows, standard/admin/UAC, startup/background, Defender/firewall, sleep/wake/network loss, crash/failure/resource testing, architecture runtime and final stability remain required.

## Phase F — Premium Privacy Protection

Status: **AUTHORIZED — ACTIVE ON `feature/premium-privacy-foundation`**

Current exact-head privacy evidence before this documentation update:

- Premium Privacy workflow run `34732339536` at `16e2dc91cbe71d3ca96275d89cf333ba40356236`: **SUCCESS**.
- Explorer acceptance: PASS.
- encryption/Vault/Secure Delete acceptance: PASS.
- native Explorer x64/x86/ARM64 builds: PASS.
- desktop build: PASS.
- unsigned x64 MSIX with Explorer extension: PASS.
- packaged Explorer extension PE verification: PASS.

### P1 — File Explorer Integration

Status: **SOURCE IMPLEMENTED / CI QUALIFIED — INSTALLED RUNTIME PENDING**

Implemented:

- native packaged `IExplorerCommand` extension;
- `Inspect with Sentinel AI` only;
- bounded filesystem selection;
- bounded one-time handoff;
- strict Sentinel-side parsing, canonicalization and revalidation;
- no encryption/deletion/network/cloud/entitlement/broker work inside Explorer;
- x86/x64/ARM64 native builds and x64 package verification.

Remaining: installed Explorer runtime matrix including install/upgrade/uninstall, restart, menu behavior, file/folder/multi-select, unsupported items, long/Unicode paths, shell crash/app unavailable and standard user.

Authoritative design: `docs/privacy/SAI-PRIV-000_Explorer_Integration.md`.

### P2 — Encrypted Container Specification

Status: **DESIGN IMPLEMENTED IN SOURCE / CI QUALIFIED — INDEPENDENT REVIEW PENDING**

Version 1 uses AES-256-GCM, fresh 256-bit per-file DEKs, unique nonce material, authenticated/versioned metadata, bounded chunked processing, strict corruption/truncation rejection, and verify-before-plaintext-removal transactions.

Authoritative design: `docs/privacy/SAI-PRIV-001_Encrypted_Container_Format.md`.

### P3 — Encryption Core

Status: **SOURCE IMPLEMENTED / CI QUALIFIED — RUNTIME/CRYPTO REVIEW PENDING**

Implemented:

- `FileEncryptionService`;
- separate encrypted output;
- bounded processing;
- flush/reopen/authenticate/verify behavior;
- structured failure handling;
- exact-owned failed-output cleanup;
- original preservation on encryption/verification failure.

Destructive in-place encryption remains prohibited.

### P4 — Key Modes and Recovery

Status: **SOURCE IMPLEMENTED / CI QUALIFIED — RUNTIME/REVIEW PENDING**

Implemented:

- Windows current-user key protection;
- portable password protection;
- independent recovery key material;
- recovery round-trip/tamper coverage;
- corrected recovery-key encoding;
- no silent escrow by design.

Authoritative design: `docs/privacy/SAI-PRIV-002_Key_Management_and_Recovery.md`.

### P5 — Sentinel Vault

Status: **SOURCE IMPLEMENTED / CI QUALIFIED FOUNDATION — RUNTIME/ADVERSARIAL REVIEW PENDING**

Implemented:

- Vault Master Key -> wrapped per-item DEK hierarchy;
- vault key protection;
- durable metadata store;
- item storage/export;
- lock/unlock race handling;
- export revocation after lock;
- exact-owned plaintext cleanup after revoked/failed export;
- metadata, item-store, export, key-foundation and recovery acceptance coverage.

Remaining: installed timeout/session-lock, filesystem faults, package upgrade, crash/concurrency, final UX and independent crypto/privacy review.

Authoritative design: `docs/privacy/SAI-PRIV-003_Vault_Architecture.md`.

### P6 — Secure Delete

Status: **AUTHORIZATION + TRANSACTION/RECOVERY FOUNDATION IMPLEMENTED / CI QUALIFIED — DESTRUCTIVE EXECUTOR PENDING**

Implemented:

- exact target validation/revalidation;
- stable volume/file identity;
- protected/system-critical/device-namespace refusal;
- reparse/directory/hard-link refusal;
- storage capability snapshot;
- short-lived mutation authorization;
- expiry/path-swap/storage-boundary revocation;
- mutation lease;
- durable operation journal;
- recovery classifier;
- tampered-journal rejection;
- extended recovery adversarial coverage.

The current coordinator remains deliberately non-destructive. `MutationGateReady` is not a path-delete primitive and overwrite sanitization remains disabled.

Next required implementation:

1. narrow exact-object destructive executor/broker boundary;
2. stable mutation-time handle retention;
3. durable transaction transitions around mutation;
4. crash recovery at each checkpoint;
5. media-specific capability strategy;
6. installed filesystem/storage runtime testing;
7. independent privacy/security review.

There must never be an unrestricted privileged `DeletePath(string path)` primitive.

Authoritative design: `docs/privacy/SAI-PRIV-004_Secure_Delete_Design.md`.

### P7 — Broad Attributable Copy / History Discovery

Status: **DESIGN COMPLETE — IMPLEMENTATION PENDING**

Search supported Windows/provider sources as broadly as safely possible and classify candidates as:

- CONFIRMED COPY
- LIKELY ATTRIBUTABLE COPY
- METADATA / REFERENCE ONLY
- UNVERIFIED CANDIDATE

Fuzzy filename similarity alone never authorizes deletion. Discovery and deletion remain separate. Reports must identify sources searched, unavailable sources, found candidates, removals, remaining items and limitations.

Authoritative design: `docs/privacy/SAI-PRIV-005_Copy_Discovery_Policy.md`.

### P8 — Explorer Privacy Commands

Status: **GATED — NOT USER-ENABLED**

Later commands:

- Encrypt File
- Add to Sentinel Vault
- Secure Delete

Explorer still only activates Sentinel. Underlying operations, entitlement and runtime qualification must complete before these verbs are exposed.

### P9 — Subscription Integration

Status: **PENDING FOR PRIVACY FEATURES**

Existing server-authoritative entitlement remains authoritative. Explorer contains no reusable entitlement secret. Entitlement never overrides filesystem safety validation.

## Premium Privacy Safety Boundary

- no unrestricted privileged path-delete primitive;
- exact-object revalidation and mutation-time identity retention;
- protected/reparse/link/race defenses;
- durable fail-closed transactions and recovery;
- discovery separate from deletion;
- fuzzy filename similarity alone never authorizes deletion;
- normal single-file Secure Delete does not directly manipulate `pagefile.sys`, `swapfile.sys`, `hiberfil.sys` or silently wipe unrelated restore/history/system data;
- do not claim guaranteed forensic irrecoverability where the platform cannot prove it;
- encryption verifies the separate encrypted output before optional plaintext removal;
- result semantics remain **VERIFIED / REQUESTED / REMAINS / CANNOT PROVE**.

## Current Next Milestones

### Hardening branch

1. Run installed Windows/UAC/adversarial matrices.
2. Execute A05 Google Cloud + Store staging.
3. Complete signed package and real-architecture qualification.
4. Run 1-hour/8-hour final stability.
5. Re-audit High findings and all 29 findings.
6. Stop at `READY FOR FINAL INDEPENDENT REVIEW`.

### Premium Privacy branch

1. Implement the narrow destructive Secure Delete executor behind exact-object handle retention and durable journal/recovery semantics.
2. Implement broad attributable-copy discovery providers and false-positive resistance.
3. Complete installed Explorer/encryption/Vault/Secure Delete runtime matrices.
4. Integrate server-authoritative privacy entitlement.
5. Enable destructive/privacy Explorer verbs only after underlying qualification.
6. Perform independent crypto/privacy review.
7. Create combined integration-validation branch only after the hardening gate permits it.

---

End of Document
