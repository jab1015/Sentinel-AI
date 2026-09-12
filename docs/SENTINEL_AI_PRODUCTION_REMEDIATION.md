# SENTINEL AI PRODUCTION REMEDIATION

Baseline assessment revision: `1218f5d39e2e98f955179d7911b013636068d373`

A checkbox is marked complete only after required source, build, runtime, and external validation is satisfied. Source implementation alone does not close a finding.

## HIGH

- [ ] SAI-A01 — Executable trust does not verify Authenticode integrity
- [ ] SAI-A02 — Quarantine is a rename, not a protected containment boundary
- [ ] SAI-A03 — Restore/delete trust attacker-editable catalog paths and hashes
- [ ] SAI-A04 — Quarantine move and catalog commit are not recoverable as one operation
- [ ] SAI-A05 — AI gateway lacks server-side authentication and entitlement enforcement
- [ ] SAI-A06 — Defender and firewall health are inferred from incomplete indicators
- [ ] SAI-A07 — Process containment loses exact identity before elevated termination
- [ ] SAI-A08 — Firewall verification can accept an allow or disabled rule
- [ ] SAI-A09 — Blocking stream reads defeat subprocess timeouts and can stall all monitoring
- [ ] SAI-A10 — Driver repair selects a package unrelated to the requested device
- [ ] SAI-A11 — Driver installation reports success even when installer reports failure
- [ ] SAI-A13 — A first-refresh exception can permanently prevent the monitoring timer starting
- [ ] SAI-A14 — Temporary cleanup trusts textual paths across a destructive race
- [ ] SAI-A16 — Service restart can affect dependencies and leave a service stopped

## MEDIUM

- [ ] SAI-A12 — Startup preference controls only one of two startup mechanisms
- [ ] SAI-A15 — Privileged actions lack a consistent execution boundary
- [ ] SAI-A17 — Caller cancellation can leave command children running
- [ ] SAI-A18 — Dormant integrity repair has inverted parsing and false verification
- [ ] SAI-A19 — AI claim validation is lexical and does not cover final response replacements
- [ ] SAI-A20 — Cloud evidence redaction misses common sensitive formats
- [ ] SAI-A21 — External research confuses keyword overlap with verified evidence and caches stale context
- [ ] SAI-A22 — External response limits apply after allocation; archive work is unbounded
- [ ] SAI-A23 — Network collection has explicit detection blind spots
- [ ] SAI-A24 — Throughput totals become invalid when adapter membership changes
- [ ] SAI-A25 — Investigation history grows indefinitely and every recent read loads it all
- [ ] SAI-A26 — Crash evidence and diagnostic writes are unreliable
- [ ] SAI-A27 — Benign-event suppression erases unrelated aggregate error evidence
- [ ] SAI-A28 — Maintenance cooldown and outcome recording fail open on persistence errors
- [ ] SAI-A29 — Architecture and test evidence do not yet support a production assurance claim

## Current checkpoint — 2026-09-11

Hardening branch: `security/production-hardening-1218f5d`.
Latest package-evidence commit at this checkpoint: `4939bcf6c678cb2515272a189371c01b1182ca33` (`ci(package): verify broker inside generated MSIX`).
Latest commit verified by the main Windows security workflow before the package-validation sequence: `11d3e0f5fcdc381b544db6dacaead8529c3a1959` (`security(A09-A23): route netstat collection through bounded runner`).

### Main Windows hardening gate

GitHub Actions run `34659474436` completed **SUCCESS** at commit `11d3e0f5fcdc381b544db6dacaead8529c3a1959`.

Commit-bound evidence accumulated on the hardening branch includes:

- Sentinel desktop x64 Release restore/build: PASS.
- Sentinel privileged broker x64 Release restore/build: PASS.
- Authenticode acceptance harness: PASS.
- Bounded-process acceptance coverage: PASS for normal completion, nonzero exit, simultaneous heavy stdout/stderr, production-style PowerShell arguments, timeout, cancellation, output caps, and descendant process-tree termination.
- System-image integrity classification harness: PASS for deterministic DISM/SFC healthy/corrupt/unknown/error classification.
- Cloud evidence redaction acceptance coverage: PASS for tested authorization/credential/JWT/user/email/MAC/device/IP/path cases.
- Benign-event filtering acceptance coverage: PASS, including preservation of unrelated error evidence.
- Investigation-history bounded retention/tail-read acceptance coverage: PASS in the recorded hardening gate.
- Diagnostic logging acceptance coverage: PASS for tested redaction, synchronous crash breadcrumb, nested exception evidence, and bounded rotation.
- The AI gateway Base64URL compiler defect identified during hardening was corrected before the current successful main Windows gate.
- `ActiveConnectionMonitor` netstat collection now routes through the common bounded process runner rather than maintaining a separate blocking process-output path.

These PASS results materially improve assurance but do not by themselves close findings that still require packaged runtime, adversarial Windows, Store, Google Cloud, architecture, or long-duration evidence.

### Store/MSIX package gate — PASS FOR UNSIGNED x64 CI PACKAGE

**STATUS: PASS for the independent unsigned x64 package-build/payload gate. This is not final Store-signed release qualification.**

- Finding: The package workflow failed before package inventory could prove the privileged broker shipped.
- Root cause: `Sentinel.App (Package).wapproj` set `GenerateTemporaryStoreCertificate=True` while the CI invocation explicitly set `AppxPackageSigningEnabled=false`. Visual Studio AppX target `_RemoveDisposableSigningCertificate` therefore invoked `RemoveDisposableSigningCertificate` without a `CertificateThumbprint`, producing `MSB4044`.
- Files changed: `.github/workflows/security-hardening-package.yml`; `src/SentinelAI/Sentinel.App/Sentinel.App (Package)/Sentinel.App (Package).wapproj`.
- Security behavior before: The package gate stopped after MSIX staging work and never produced accepted CI evidence that the final generated application package contained the privileged broker.
- Security behavior after: Temporary Store certificate generation is disabled for the unsigned CI package. The workflow builds the x64 MSIX, locates the generated application MSIX, unpacks that exact package with Microsoft `MakeAppx.exe`, requires exactly one `Sentinel.App.exe` and exactly one `Sentinel.PrivilegedBroker.exe`, hashes the MSIX and both executables, and uploads a payload-inventory artifact.
- Tests added: Diagnostic MSBuild log capture; generated-MSIX unpack and exact executable-count verification; package/MSIX/application/broker SHA-256 inventory artifact.
- Tests passed: Run `34663107498` proved the minimal certificate fix allows the unsigned x64 package build to complete. Run `34663342919` at commit `4939bcf6c678cb2515272a189371c01b1182ca33` completed **SUCCESS**, including `Build unsigned x64 MSIX staging package`, `Verify generated MSIX payload`, `Upload package payload inventory`, and `Record package inventory`.
- Package evidence: generated MSIX SHA-256 `B0392ACE0F2C35474B20AF762EA0401AA1F8759120BF6ABBD442FB40DE163B3D`; unpacked `Sentinel.App.exe` SHA-256 `A78E3ED29B961298341B796A8CA92C443B50791F26608E59D8700E9FABE90E70`; unpacked `Sentinel.PrivilegedBroker.exe` SHA-256 `578198BA009E4DB5ACB4732E08637D77C0703640E277BB5235BB0B2B8DF8DAE8`. Artifact `package-payload-inventory` ID `10288417134`, retained for 30 days by the workflow.
- Tests failed: The earlier diagnostic run intentionally reproduced the original failure and captured `MSB4044`; no failure occurred in the final package-payload gate above.
- Remaining concerns: The package is an unsigned CI staging package. Store-signed provenance, clean install, upgrade, uninstall, packaged runtime/UAC behavior, shipped architectures, and final release qualification remain open.
- Related findings discovered: none from the proven package failure itself; package provenance/runtime evidence remains relevant to SAI-A07/SAI-A15/SAI-A29.
- Recommended next action: Continue A02/A03/A04 quarantine hardening, then execute packaged broker/UAC adversarial validation and final Store/release qualification.

## Finding records

### SAI-A01 — Authenticode executable trust

**STATUS: NEEDS MORE WORK**

- Finding: Executable trust did not verify Authenticode integrity.
- Root cause: Certificate extraction and certificate-chain checks were treated as equivalent to full-file Authenticode verification; publisher substring matching could influence trust; cache identity was not bound to file content.
- Files changed: `AuthenticodeVerifier.cs`, `ProcessMonitor.cs`, Authenticode acceptance harness.
- Security behavior before: A signed-looking or publisher-lookalike binary could avoid the intended warning without Windows establishing full-file Authenticode trust.
- Security behavior after: Process trust decisions require Windows Authenticode verification; changed-during-verification files fail closed; the process signature cache is content-bound; unsigned remains a signal rather than malware proof. Current source also includes Windows catalog-signature handling rather than limiting trust verification to embedded signatures.
- Tests added: Trusted Windows executable, unsigned executable, tampered trusted copy, structured status mapping.
- Tests passed: Recorded Windows CI passes the Authenticode acceptance scenarios.
- Tests failed: None in the recorded Authenticode harness run.
- Remaining concerns: Catalog-signed fixture behavior, self-signed publisher lookalikes, timestamp-preserving replacement, revocation/network behavior, timestamped signatures, and x86/ARM64 release behavior still require adversarial/runtime validation.
- Recommended next action: Extend the Windows fixture matrix and keep A01 open until catalog/timestamp/revocation and shipped-architecture behavior are proven.

### SAI-A05 — AI gateway authentication and entitlement enforcement

**STATUS: NEEDS MORE WORK — BLOCKED — EXTERNAL VALIDATION REQUIRED**

- Finding: The AI gateway accepted direct analysis calls without authenticated identity or server-side paid entitlement enforcement.
- Root cause: Microsoft Store licensing was checked only in the desktop client; the gateway trusted caller-selected model tier and relied primarily on network-level rate limiting.
- Files changed: `GatewaySecurity.cs`, gateway `Program.cs`, desktop gateway/Store integration, gateway security acceptance harness.
- Security behavior before: A caller that could reach the endpoint could bypass the desktop subscription check and request paid provider usage.
- Security behavior after: Source implements short-lived signed sessions, server-side tier enforcement, Microsoft Store entitlement verification for paid sessions, replay request IDs, provider concurrency limiting, and server-side provider credentials. The earlier Base64URL compile blocker was corrected and the current main Windows gate is green.
- Remaining concerns: Google Cloud IAM, managed-secret configuration, production Store/Partner Center association, distributed account/spend controls, key rotation, live entitlement response shape, and staging abuse tests remain external validation requirements.
- Related findings: SAI-A19, SAI-A20, SAI-A21, SAI-A22.
- Recommended next action: Validate anonymous/tampered/expired/replayed/Basic-to-Advanced/unsubscribed requests in staging, then validate the live Store entitlement path without uncontrolled provider spend.

### SAI-A15 / SAI-A07 — privileged execution boundary and process identity

**STATUS: NEEDS MORE WORK — PACKAGE PRESENCE PROVEN**

- Finding: Privileged actions lacked a consistent execution boundary and process termination could lose exact target identity across UAC delay.
- Security behavior after: The hardening branch uses a versioned allowlisted named-pipe broker protocol, current-user pipe restriction, named-pipe peer PID verification, sibling Sentinel application executable verification, and execution-time process start/path/hash validation for termination. Cancellation/timeout paths do not report success and terminate the launched broker child where possible.
- Build/package evidence: Desktop and broker x64 Release builds pass in Windows CI. Run `34663342919` unpacked the generated x64 MSIX and proved both `Sentinel.App.exe` and `Sentinel.PrivilegedBroker.exe` are inside the package payload.
- Remaining concerns: Adversarial Windows runtime validation for unauthorized same-user callers, executable/client spoofing, malformed/extra fields, arbitrary-command attempts, UAC accept/deny, broker signing/provenance, ACL behavior, cancellation races, PID reuse/target changes during approval, and packaged install-path behavior.
- Recommended next action: Add broker IPC/UAC adversarial runtime fixtures against the packaged build.

### SAI-A09 / SAI-A17 — bounded subprocess ownership

**STATUS: NEEDS MORE WORK — SOURCE/CI SUBSTANTIALLY VALIDATED**

- Security behavior after: The common runner drains stdout/stderr concurrently, enforces wall-clock timeout, caps captured output, returns structured outcomes, and terminates process trees on timeout/cancellation where Windows permits it. Driver research and netstat collection now use the bounded runner rather than blocking `ReadToEnd()` implementations.
- Tests passed: Normal completion, nonzero exit, heavy simultaneous output, production-style PowerShell launch, timeout, cancellation, output truncation, and descendant-tree termination have passed in Windows CI.
- Remaining concerns: Re-audit every process-launch helper for bypasses and validate access-denied termination/packaged runtime behavior before final closure.

### SAI-A12 — startup behavior

**STATUS: NEEDS MORE WORK — SOURCE/BUILD IMPROVED**

The branch has moved toward a single packaged StartupTask mechanism and added single-instance activation/redirect behavior. The desktop Release build passes with these changes. Packaged install/startup, Windows-disabled startup preference, duplicate activation, Explorer restart, and Store lifecycle behavior still require runtime validation.

### SAI-A14 — temporary cleanup path race

**STATUS: NEEDS MORE WORK**

Automatic temporary-file cleanup currently fails closed and performs no deletion because a handle-based, reparse-resistant deletion primitive has not yet been validated. This removes the unsafe destructive behavior from the current branch, but the optimization capability is intentionally incomplete and A14 remains open.

### SAI-A16 — service restart dependency safety

**STATUS: NEEDS MORE WORK**

Automatic service restart currently fails closed rather than stopping a service through the old unsafe path. Dependency-aware broker execution, rollback, cancellation recovery, and final running-state verification are still required before this capability can be restored and A16 closed.

### SAI-A18 — system image integrity classification

**STATUS: NEEDS MORE WORK — SOURCE/CI SUBSTANTIALLY VALIDATED**

The original inverted DISM phrase matching has been removed. Explicit healthy/corrupt/unknown/error states are used, and deterministic DISM/SFC classification regression tests pass in Windows CI. Runtime DISM/SFC behavior, cancellation, reboot, permission, and supported-Windows-version validation remain before release closure.

### SAI-A19 — final Ask Sentinel claim boundary

**STATUS: NEEDS MORE WORK**

Lexical phrase matching is no longer treated as authorization for security claims, and cloud model prose is kept advisory rather than copied directly into security-state claims. However, `MainWindow` can still replace a response after the orchestrator's validation step for optimization, external-investigation, and driver-answer paths. The final displayed response therefore needs a deterministic final validation/provenance boundary after all composition/replacement before A19 can close.

### SAI-A20 — cloud evidence redaction

**STATUS: NEEDS MORE WORK — DETERMINISTIC TESTS PASS**

Acceptance coverage passes for the tested authorization header, credential, JWT-like token, user/email, MAC, device identifier, IP, and filesystem path cases. Continue adversarial redaction testing and deployed telemetry/log inspection; no secret-safety claim should depend only on regex tests.

### SAI-A21 — external research evidence

**STATUS: NEEDS MORE WORK**

The original keyword-overlap path no longer promotes external research to `Verified=true`; matches are treated as potentially relevant/advisory. Passage-to-claim provenance, stale-cache behavior, and regression coverage still need final validation.

### SAI-A22 — bounded external/archive work

**STATUS: NEEDS MORE WORK**

Source adds bounded response/archive/XML handling and routes driver catalog expansion through bounded process execution. Closure still requires malformed/oversized archive, decompression/file-count, redirect/source-policy, timeout, and resource-exhaustion tests.

### SAI-A23 — network collection coverage

**STATUS: NEEDS MORE WORK**

The branch now explicitly represents polling/coverage limitations, and netstat collection uses the bounded process runner. Short-lived TCP, UDP peer attribution, LAN/common-port correlation, IPv6/QUIC behavior, process attribution, adapter churn, and event-driven coverage remain runtime validation/feature limitations and must not be described as comprehensive blocking coverage.

### SAI-A24 / A25 / A26 / A27 / A28 — correctness, persistence, and diagnostics

**STATUS: NEEDS MORE WORK — MULTIPLE DETERMINISTIC TESTS PASS**

Source remediation includes per-adapter throughput baselines/reset handling, bounded investigation-history retention/tail reads, serialized/redacted diagnostic logging with crash breadcrumbs and rotation, benign-event filtering before aggregate error counting, and fail-closed maintenance persistence with durable pre-action reservation. Recorded CI has passed A25 history, A26 diagnostic, and A27 benign-filter acceptance coverage. Continue concurrency/fault/runtime validation, particularly for A24 and A28, before closing the group.

### SAI-A29 — architecture/test/release assurance

**STATUS: NEEDS MORE WORK — UNSIGNED x64 PACKAGE GATE PASSES; RELEASE QUALIFICATION OPEN**

The independent x64 package gate now builds and unpacks the generated MSIX and proves both the desktop app and privileged broker are in the actual package payload. This removes the immediate package-build blocker but does not constitute production sign-off. A29 remains open until final-commit signed/Store-style package, clean install/upgrade/uninstall, shipped-architecture, supported-Windows, standard/admin/UAC, startup/background, Defender/firewall, recovery/failure, resource, and fresh 1-hour/8-hour stability evidence is commit-bound.

## Remaining work before independent Astra re-evaluation

1. Finish A02/A03/A04 quarantine boundary adversarial validation and remaining handle/file-identity TOCTOU/crash-recovery corrections.
2. Finish A07/A15 broker IPC/UAC/package runtime validation.
3. Implement/validate a safe handle-based A14 cleanup primitive or keep the feature explicitly disabled for release.
4. Implement/validate dependency-aware A16 service remediation or keep automatic restart explicitly disabled for release.
5. Close A19 by validating the final response after all UI composition/replacement.
6. Add hostile A22 archive/response tests and remaining A21 evidence/cache tests.
7. Validate A23/A24 network/throughput behavior on real Windows and document unavoidable coverage limitations honestly.
8. Complete Google Cloud + Microsoft Store external validation for A05 and inspect deployed logs/redaction for A20.
9. Perform adversarial re-audit of all original High findings, then all 29 findings.
10. Run signed/package install-update-uninstall, supported Windows/architecture, startup/background/UAC/Defender/firewall/sleep-wake/network-loss/crash-recovery tests.
11. Run fresh commit-bound 1-hour and 8-hour stability/resource tests.
12. Only after those gates are satisfied, hand the resulting final commit to Astra for an independent production/security re-evaluation.

## Release closure rules

Do not call Sentinel AI production-hardened merely because source changes exist or the main CI workflow is green. After all 14 High findings have source corrections and their required validation, perform a fresh adversarial re-audit against every original High finding. After all 29 findings, perform a complete source re-audit. Before release, require commit-bound Windows build/runtime evidence, packaged Store evidence, backend configuration evidence, 1-hour and 8-hour stability artifacts, shipped-architecture validation, and known-limitations review.
