# SENTINEL AI PRODUCTION REMEDIATION

Baseline assessment revision: `1218f5d39e2e98f955179d7911b013636068d373`  
Hardening branch: `security/production-hardening-1218f5d`  
Last Updated: 2026-09-12

A finding is complete only after all required source, deterministic, adversarial, Windows runtime, package, Store, cloud, architecture, and stability validation is satisfied. **Source implementation or green CI alone does not close a finding.**

## Status vocabulary

- **OPEN — CODE NOT COMPLETE**
- **SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**
- **BLOCKED — GOOGLE CLOUD VALIDATION REQUIRED**
- **BLOCKED — MICROSOFT STORE / PARTNER CENTER VALIDATION REQUIRED**
- **BLOCKED — WINDOWS RUNTIME VALIDATION REQUIRED**
- **PASS — FULLY VERIFIED**

## Current checkpoint

- Last fully proven broad checkpoint: `3ef08da9226e33a222768938b3dff13373ba7f61`.
- Latest source-hardening checkpoint before this documentation synchronization: `cd98180f8ecaf83272f821d2e7065d72d5953395`.
- Release posture: **NOT production-hardened; DO NOT MERGE**.

### Proven CI evidence

Exact-head broad CI for `3ef08da9226e33a222768938b3dff13373ba7f61`:

- Windows hardening `34677065591`: **SUCCESS**.
- Unsigned x64 package `34677065599`: **SUCCESS**.
- Driver-repair hardening `34677065596`: **SUCCESS**.
- Optimization-state hardening `34677065594`: **SUCCESS**.

Additional focused evidence after that checkpoint:

- A21 external-research provenance workflow `34678939880` on `92554edaf28d85e6221fcccf0734ef88751a689d`: **SUCCESS** after correcting distinct-term passage attribution.
- A22 child-process safety workflow `34679015786` on `03048af9fecb1733cba3e57fc37ceb67d8964bb0`: **SUCCESS**; `expand.exe` is rejected before launch while ordinary bounded children remain allowed.
- A21 investigation-cache workflow `34679063934` on `019ddedfc5953c85e9f59d7c1595350d3ff833d3`: **SUCCESS**; expired/stale entries are not returned as current.
- Focused driver-repair and external-research workflows were also successful on the later `69c43e68a529a1eaaab1149488c4314ef283f574` checkpoint before the latest source corrections.

The exact-head workflows generated for `cd98180f8ecaf83272f821d2e7065d72d5953395` were **QUEUED** at the latest observation, including broad Windows CI, A17 subprocess-boundary, A10/A11 driver-repair policy, A21 external-research provenance, A22 child-process safety, A28 optimization-state, x86/ARM64 package architecture, x64 package, and architecture builds. Queued/running checks are not counted as PASS.

## Original findings and live status

### HIGH

### SAI-A01 — Executable trust does not verify Authenticode integrity
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Windows trust verification is content-bound and deterministic acceptance coverage exists. Remaining: valid Microsoft embedded and catalog signatures, unsigned, tampered, self-signed/untrusted, expired with/without valid timestamp, revoked/offline-revocation cases, replacement races, timestamp-preserving replacement, cache invalidation, and every shipped architecture. Unsigned must not automatically mean malicious.

### SAI-A02 / A03 / A04 — quarantine containment, trusted state, crash recovery
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Protected records/transactions, record-derived restore identity, semantic recovery guard, path/hash/link checks, reparse defenses, no-overwrite restore, exact-payload delete, and cleanup-failure preservation have deterministic coverage. Remaining: standard-user ACL/owner resistance, parent replacement, link/reparse races, hardlinks, source mutation, destination races, orphan/crash checkpoints, disk-full/access-denied, broker termination, and installed UAC/package behavior.

### SAI-A05 — AI gateway authentication and entitlement
**STATUS: BLOCKED — GOOGLE CLOUD VALIDATION REQUIRED / BLOCKED — MICROSOFT STORE / PARTNER CENTER VALIDATION REQUIRED**

Source includes signed short-lived sessions, server-side entitlement/tier checks, replay IDs, concurrency/rate controls, and server-side provider credentials. Remaining staging includes no/malformed/expired/modified authentication, replay, tier escalation, unsubscribed/expired/revoked entitlement, rate/high-concurrency/provider-timeout/secret-unavailable/Store-outage/restart tests, multi-instance distributed replay/rate state, IAM, Secret Manager, environment configuration, logging/alerts, and spend controls.

### SAI-A06 — Defender/firewall health inferred from incomplete indicators
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Deterministic classification fails closed for passive/disabled/stale/incomplete Defender evidence and stopped/partial/incomplete firewall evidence. Remaining installed Windows/Defender variants, policy-managed/passive states, service transitions, unavailable evidence, and supported-Windows runtime validation.

### SAI-A07 — exact elevated process identity
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Broker/client identity is bound to package full name, sibling path, peer PID, exact target start/path/hash, protocol/request IDs, strict request shape, same-user constraints, and allowlisted operations. Remaining installed packaged desktop -> elevated broker, UAC cancellation, hostile same-user callers, copied/spoofed siblings, package mismatch, malformed/oversized/extra-field IPC, unsupported protocol/operation, injection attempts, arbitrary paths, PID reuse/target replacement, caller exit, no-connect/no-send/disconnect/hanging clients, pipe races/second caller, upgrade-pending packages, ACL/signature/package SID behavior. If elevation loses required package identity, fail closed.

### SAI-A08 — firewall verification false success
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Firewall mutation uses fixed-shape broker operations and literal IP validation. Verification requires the exact intended enabled outbound Block rule and fails closed on malformed/incomplete evidence; creation rolls back if exact post-verification fails and removal must verify actual absence. Remaining Group Policy/concurrent mutation, IPv4/IPv6, installed UAC path, and real containment/unblock verification.

### SAI-A09 — blocking subprocess reads defeat timeout
**STATUS: OPEN — CODE NOT COMPLETE**

The common bounded runner drains stdout/stderr concurrently, caps output, enforces wall-clock timeout/cancellation, owns/terminates child trees, bounds post-kill waits/reads, and returns structured outcomes. The last eight direct service-process bypasses identified by the live audit have now been migrated. Source-complete status is intentionally withheld until the exact-head subprocess-boundary audit and broad Windows integration are green. Runtime kill/access-denied/package behavior remains after source closure.

### SAI-A10 / A11 — driver repair identity and false success reporting
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Repair is bound to exact PnP instance, hardware ID, Windows Update ID/revision, installer/per-update result, HRESULT, restart state, and post-install verification. Adversarial re-review found a remaining false-success case: a healthy device with the update no longer offered could be reported verified without proving the installed driver version actually changed. Source now requires known before/after driver versions and an actual version change; missing or unchanged versions fail closed. The deterministic harness covers exact match, ambiguous update, wrong device/hardware identity, changed update identity/revision through pre-install revalidation, installer/HResult/result failures, restart required, device identity change, still-offered update, problem code, missing health evidence, and unchanged/missing driver version. Exact-head focused CI is queued. Remaining real Windows Update/device/runtime testing is mandatory.

### SAI-A13 — first-refresh exception prevents monitoring timer
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Timer startup is guaranteed after initial refresh failure and deterministic startup coverage exists. Remaining installed startup/background lifecycle, repeated failures/recovery, suspend/resume, sleep/wake, session transitions, and long-run validation.

### SAI-A14 — destructive temporary cleanup path race
**STATUS: OPEN — CODE NOT COMPLETE**

Automatic temporary deletion remains intentionally disabled/fail-closed. It must not be enabled until exact-object, ownership/path, handle-based/reparse-resistant/race-resistant deletion and adversarial runtime tests exist.

### SAI-A16 — unsafe service restart
**STATUS: OPEN — CODE NOT COMPLETE**

Automatic service restart remains intentionally disabled/fail-closed. Dependency-aware broker execution, rollback, cancellation/crash recovery, privilege safeguards, and verified final service state are required before enabling it.

## MEDIUM

### SAI-A12 — startup preference vs startup mechanisms
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Packaged StartupTask/single-instance direction is implemented. Remaining enable/disable, Windows-disabled preference, duplicate activation, Explorer restart, Store upgrade, and lifecycle validation.

### SAI-A15 — privileged actions lack consistent boundary
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Covered with A07 broker hardening. Installed UAC/adversarial runtime matrix remains mandatory.

### SAI-A17 — cancellation can leave child processes running
**STATUS: OPEN — CODE NOT COMPLETE**

The common `BoundedProcessRunner` provides wall-clock timeout, cancellation, child-tree ownership/termination, concurrent bounded stdout/stderr draining, bounded post-kill waits/reads, structured failure classification, and no false-success semantics. The source audit remains intact and rejects redirected subprocess ownership outside this boundary.

The latest audit had identified eight remaining service bypasses. They have now been corrected without weakening the audit:

- `BootStartupImpactCorrelationService` (`wevtutil.exe`)
- `StorageOptimizationAssessmentService` (PowerShell / `fsutil.exe`)
- `StorageOptimizationExecutor` (`defrag.exe`)
- `WindowsUpdateRepairExecutor` (`sc.exe`)
- `PowerPlanOptimizationExecutor` (`powercfg.exe`)
- `StorageOptimizationPlanService` (`defrag.exe` analysis)
- `WindowsServiceRepairExecutor` (`sc.exe`)
- `NetworkHealthAssessmentService` (`nslookup.exe`)

Other previously migrated monitoring/diagnostic paths remain on the common runner, including network repair, crash-dump analysis, command-line, firewall-rule, WMI persistence, driver monitoring/evidence, Windows service health, scheduled tasks, device health, Windows Update diagnostics, boot diagnostics, and related collectors. Crash-dump conclusions fail closed on truncated output.

Remaining before source-complete status: exact-head subprocess-boundary CI and broad Windows build/integration. Runtime access-denied/kill-failure/package behavior remains after source closure. User-facing shell activation is not a Sentinel-owned diagnostic subprocess and is reviewed separately.

### SAI-A18 — system image integrity classification
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

DISM/SFC healthy/corrupt/unknown/error classification has deterministic coverage. The current System Image Health acceptance harness now links the common runner's `ChildProcessSafetyPolicy`, correcting the latest observed Windows-CI compile dependency. Remaining exact-head CI and real permissions/cancellation/reboot/supported-Windows/failure validation.

### SAI-A19 — final Ask Sentinel claim boundary
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Every final/replaced/composed displayed answer is revalidated; advisory/inferred text cannot claim blocked/quarantined/repaired/Defender/firewall action without verified action evidence. Remaining installed UI paths, real service replacements, adversarial model text, timeout/cancellation, and final provenance UX validation.

### SAI-A20 — cloud evidence redaction
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Deterministic sensitive-format tests pass. Remaining adversarial formats and deployed logging/telemetry inspection.

### SAI-A21 — external research keyword overlap / stale context
**STATUS: OPEN — CODE NOT COMPLETE**

Production external research uses bounded attributable passages rather than page-level keyword overlap. Distinct evidence terms retain independent attribution even when sharing one bounded source passage. External evidence remains advisory and cannot become verified local-machine/action evidence. Driver research distinguishes a known configured official authority from a source Sentinel actually reached. A dedicated cache harness covers fresh reads, expiry, explicit invalidation, type mismatch, and expired-entry cleanup. Focused provenance/cache gates are proven green.

Adversarial review found an additional catalog-to-package authority defect: an absolute URL read from Dell catalog content could previously be surfaced as a "Dell-hosted" package candidate without independently proving Dell HTTPS authority. Source now routes catalog package paths through `ExternalResearchProvenancePolicy.TryResolveDellPackageUri`: relative paths are anchored to `https://downloads.dell.com/`, and plain HTTP, foreign hosts, alternate ports, credential-bearing URLs, and non-EXE targets fail closed. The production parser uses the same policy. Deterministic provenance coverage includes all of those cases. Exact-head focused/broad CI is queued.

Remaining before source closure: exact-head validation and final all-call-path review of authority, bounded passage count/length, duplicate/stale handling, advisory labeling, and final Ask Sentinel safety semantics.

### SAI-A22 — external allocation/archive work unbounded
**STATUS: OPEN — CODE NOT COMPLETE**

External body/catalog reads are bounded while streaming, XML parsing has character limits, child commands are time/output bounded, HTTPS is required, redirects are disabled, and response authority/port is revalidated. The prior Dell CAB flow could consume arbitrary temporary disk during `expand.exe` before post-expansion checks; the common child-process policy blocks `expand.exe`. The driver-research path now performs that expansion-policy preflight before even downloading the Dell catalog, so the unsafe CAB is not downloaded or expanded while extraction remains unsupported. Deterministic preflight coverage exists.

Dell package URLs parsed from catalog content are additionally authority-pinned as described in A21 so future safe catalog parsing cannot silently cross to an attacker-controlled package authority. Automatic CAB expansion must remain disabled until a design can bound expansion size/entry count/nesting/filesystem growth before and during extraction.

Remaining before source closure: exact-head Windows/package compilation and final malformed/oversized/decompression/resource/cancellation/cleanup/temp-file review.

### SAI-A23 — network collection blind spots
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Polling limitations are explicit and owned netstat execution uses the bounded runner. Remaining short-lived TCP, UDP attribution, LAN/common-port correlation, IPv6/QUIC, process attribution, adapter churn, and event-driven coverage. Do not claim comprehensive independent network blocking.

### SAI-A24 — throughput invalid under adapter membership changes
**STATUS: OPEN — CODE NOT COMPLETE**

`NetworkThroughputPolicy` calculates each surviving adapter against its own baseline interval and sums valid per-adapter rates. New adapters establish a baseline without a spike; counter resets, non-positive time deltas, and invalid timer frequency fail closed. Dedicated churn/reset coverage exists. Remaining before source closure: current exact-head dedicated/full Windows CI, followed by installed adapter churn/sleep-wake/network-transition runtime validation.

### SAI-A25 — unbounded investigation history
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Bounded history retention/tail reads have deterministic coverage. Runtime/concurrency/fault validation remains where applicable.

### SAI-A26 — unreliable crash/diagnostic evidence writes
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Serialized/redacted diagnostics, rotation, and crash breadcrumbs have deterministic coverage. Runtime filesystem/failure validation remains.

### SAI-A27 — benign suppression erases unrelated aggregate evidence
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Filtering occurs before aggregate counting and deterministic coverage exists. Runtime validation remains where applicable.

### SAI-A28 — cooldown/outcome persistence fails open
**STATUS: SOURCE COMPLETE — RUNTIME VALIDATION REQUIRED**

Safety-relevant optimization persistence now fails closed on corrupt, unreadable, oversized, inconsistent, or existing all-default state rather than treating it as a fresh installation. Existing persisted state requires `LastAttemptUtc`; a success timestamp newer than the attempt is rejected. State reads are bounded to 64 KiB, summaries are bounded, writes use an exclusive temporary file with disk flush, and the entire written record is re-read and compared before success is reported. Sentinel still durably reserves `LastAttemptUtc` before executor invocation and preserves the pre-action reservation if post-action summary persistence fails.

A cross-process exclusive lease now spans load, cooldown evaluation, pre-action reservation, execution, and final persistence so two Sentinel processes cannot consume the same cooldown window concurrently. The deterministic harness covers missing first-run state, exact reservation/completion save and reload, malformed JSON, `{}` truncation/default state, invalid timestamp ordering, oversized state, locked persistence, concurrent lease exclusion, and lease reacquisition. Exact-head optimization-state CI is queued.

Remaining installed/runtime validation includes filesystem permission/save/load/corruption, forced crash/restart recovery, stale/future clock behavior, process termination while the lease is held, atomic-replacement fault behavior, and long-run validation.

### SAI-A29 — architecture/release assurance incomplete
**STATUS: BLOCKED — WINDOWS RUNTIME VALIDATION REQUIRED / BLOCKED — MICROSOFT STORE / PARTNER CENTER VALIDATION REQUIRED**

Sentinel intentionally declares x86, x64, and ARM64. Direct Release builds for the declared architectures had succeeded, while the new x86/ARM64 package gate failed when validating packaged executable PE machine architecture. This narrowed the immediate source issue to package/child-project architecture propagation rather than compiler support. Desktop and broker projects now map package `Platform` (`x86`, `x64`, `ARM64`) to the matching .NET `RuntimeIdentifier` (`win-x86`, `win-x64`, `win-arm64`) when one is otherwise absent. The package architecture verification was not weakened. Exact-head x86/ARM64 package jobs are queued and must determine whether further WAP `ProjectReference` propagation is required.

Even if cross-build/package gates become green, they provide build/package evidence only. Remaining qualification includes signed/Store package provenance, clean install/upgrade/uninstall, real x86/x64/ARM64 runtime for every architecture actually shipped, supported Windows versions, standard/admin/UAC, startup/background, Defender/firewall, sleep/wake/network loss, recovery/failure/resource behavior, and fresh final-commit 1-hour/8-hour stability evidence.

## Current work order

1. Read current exact-head broad Windows, A17 subprocess-boundary, A10/A11 driver, A21/A22, A28, and A29 package-architecture results as runners complete; correct real failures narrowly.
2. Move A09/A17 to source-complete only after exact-head audit/integration is green.
3. Validate the strengthened A10/A11 no-false-success driver policy on exact-head focused CI.
4. Finish A29 x86/ARM64 package architecture without deleting supported architectures or weakening PE verification.
5. Finish A21/A22 full-path/resource review and current integration evidence; keep CAB expansion disabled.
6. Finish A24 exact-head CI then installed adapter-churn runtime validation.
7. Complete A07/A15 installed broker/UAC adversarial validation.
8. Complete A01 extended Authenticode runtime fixture/architecture matrix.
9. Execute A05 Google Cloud + Microsoft Store entitlement staging validation.
10. Complete remaining runtime matrices for A06/A08/A10/A11/A13/A18/A19/A20/A23/A25-A28.
11. Complete A29 signed Store release qualification.
12. Run fresh final-commit 1-hour and 8-hour stability/resource tests.
13. Re-audit every High finding adversarially, then all 29 findings.
14. Only then label the branch `READY FOR FINAL INDEPENDENT REVIEW`.

## Premium Privacy Protection preservation gate

The Premium Privacy Protection roadmap in `SAI-005_Product_Roadmap.md` and `SAI-000_Project_Status.md` remains authoritative and **PLANNED POST-HARDENING**. Current hardening must not remove, weaken, overwrite, or prematurely implement it.

Planned subscription-only scope remains: File Explorer integration, Inspect with Sentinel AI, Secure Delete, Encrypt File, Sentinel Vault, attributable-copy/history discovery, Windows-account protection, password-portable encryption, independent recovery keys, optional future Windows Hello/TPM integration, and server-side premium entitlement.

Secure Delete remains constrained to the strongest **safe** privacy deletion Sentinel can provide for an explicitly selected user file and reliably attributable copies/references. It must never become an unrestricted privileged delete primitive. Required controls include explicit user intent, exact target identity/immediate revalidation, canonical paths, protected-location checks, reparse/junction/link defenses, race resistance, storage/media awareness, fail-closed transactions, post-operation verification, and structured evidence. Media behavior must distinguish HDD, SATA SSD, NVMe/flash, NTFS, BitLocker, and cloud-sync realities. Results must distinguish **VERIFIED**, **REQUESTED**, **REMAINS**, and **CANNOT PROVE**. Attributable-copy/history discovery is separate from deletion. Normal single-file Secure Delete must not directly modify `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys`, wipe unrelated restore/history sets, or silently alter system databases. Do not claim guaranteed forensic irrecoverability without proof.

Encryption remains AES-256-GCM with a fresh random per-item DEK, unique nonce material, authenticated metadata, a versioned container, corruption detection, and reviewed streaming/chunked large-file handling. Safe transaction order remains: validate source -> create separate encrypted output -> complete/flush -> reopen/authenticate/verify -> only then optionally remove plaintext. Failure preserves the original; plaintext-removal failure preserves the encrypted copy and reports that plaintext remains. Windows-account mode wraps independent keys with current-user protection. Password mode uses a maintained reviewed KDF, preferably Argon2id where supportable, with random salt/versioned parameters. Recovery keys are independent/high-entropy with Copy/Save/Print and no silent escrow. Sentinel Vault uses a master key to wrap independent per-item keys and supports automatic/session lock plus account/password/recovery modes. Avoid plaintext temporary extraction.

Explorer integration remains thin and non-privileged: identify selected shell items and activate Sentinel only. It must not directly encrypt/delete, contact subscription services, scan, or invoke the broker. Sentinel must reopen/revalidate the target.

Preferred future implementation order remains: harmless Explorer integration -> encrypted-container specification -> encryption core -> recovery -> Sentinel Vault -> primary Secure Delete -> attributable-copy discovery/cleanup -> advanced privacy -> independent crypto/privacy review -> Windows storage/runtime validation.

## Release closure rule

**Current merge recommendation: DO NOT MERGE.**

Do not call Sentinel AI production-hardened because source changes exist or CI is green. Production readiness requires commit-bound proof across every required source, Windows runtime, package/Store, cloud, architecture, adversarial, and stability gate.
