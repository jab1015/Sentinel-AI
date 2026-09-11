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

## Current commit-bound validation evidence

Windows GitHub Actions has now established the following on the hardening branch:

- Sentinel desktop x64 Release restore/build: PASS, zero compiler warnings/errors in the recorded run.
- Sentinel privileged broker x64 Release restore/build: PASS, zero compiler warnings/errors in the recorded run.
- Authenticode acceptance harness restore/build/run: PASS.
- Authenticode cases proven in CI: trusted Windows executable, unsigned executable, tampered trusted copy rejected, structured status mapping.
- A dedicated bounded-process acceptance harness has been added for normal completion, nonzero exit, simultaneous heavy stdout/stderr, timeout, cancellation, and output caps. Its CI result must be recorded before A09/A17 can close.
- AI gateway security acceptance harness exists, but the current recorded gate is BLOCKED by a compile error in the Base64URL padding expression. Do not claim gateway test success until a later commit-bound run passes.

## Finding records

### SAI-A01 — Authenticode executable trust

**STATUS: NEEDS MORE WORK**

- Finding: Executable trust did not verify Authenticode integrity.
- Root cause: Certificate extraction and certificate-chain checks were treated as equivalent to full-file Authenticode verification; publisher substring matching could influence trust; cache identity was not bound to file content.
- Files changed: `AuthenticodeVerifier.cs`, `ProcessMonitor.cs`, Authenticode acceptance harness.
- Security behavior before: A signed-looking or publisher-lookalike binary could avoid the intended warning without Windows establishing full-file Authenticode trust.
- Security behavior after: Process trust decisions require Windows Authenticode verification; changed-during-verification files fail closed; the process signature cache is content-bound; unsigned remains a signal rather than malware proof.
- Tests added: Trusted Windows executable, unsigned executable, tampered trusted copy, structured status mapping.
- Tests passed: All four Authenticode acceptance scenarios passed in Windows CI on the hardening branch.
- Tests failed: None in the recorded Authenticode harness run.
- Remaining concerns: Catalog-signed binaries, self-signed publisher lookalikes, timestamp-preserving replacement, revocation/network behavior, timestamped signatures, and x86/ARM64 release behavior still require adversarial/runtime validation.
- Related findings discovered: None beyond original A01 scope.
- Recommended next action: Extend the Windows fixture matrix and keep A01 open until catalog/timestamp/revocation and shipped-architecture behavior are proven.

### SAI-A05 — AI gateway authentication and entitlement enforcement

**STATUS: NEEDS MORE WORK — BLOCKED — EXTERNAL VALIDATION REQUIRED**

- Finding: The AI gateway accepted direct analysis calls without authenticated identity or server-side paid entitlement enforcement.
- Root cause: Microsoft Store licensing was checked only in the desktop client; the gateway trusted caller-selected model tier and relied primarily on network-level rate limiting.
- Files changed: `GatewaySecurity.cs`, gateway `Program.cs`, desktop gateway/Store integration, gateway security acceptance harness.
- Security behavior before: A caller that could reach the endpoint could bypass the desktop subscription check and request paid provider usage.
- Security behavior after: Source now implements short-lived signed sessions, server-side tier enforcement, Microsoft Store entitlement verification for paid sessions, replay request IDs, provider concurrency limiting, and server-side provider credentials.
- Tests added: Gateway security acceptance harness covers session/tier/replay security behavior.
- Tests passed: Not yet established. The latest recorded Windows gate reached this harness but failed while compiling `GatewaySecurity.cs` at the Base64URL padding expression.
- Tests failed: Gateway harness build currently fails with a C# operator-precedence compile error; harness execution is therefore skipped.
- Remaining concerns: Correct the compile blocker, then validate anonymous/tampered/expired/replayed/Basic-to-Advanced/unsubscribed requests. Google Cloud IAM, managed-secret configuration, production Store/Partner Center association, distributed account/spend controls, key rotation, and live entitlement shape remain external validation requirements.
- Related findings: SAI-A19, SAI-A20, SAI-A21, SAI-A22.
- Recommended next action: Clear the compile blocker, run deterministic gateway tests, then validate the Store entitlement path in staging without provider spend before production deployment.

### SAI-A15 / SAI-A07 — privileged execution boundary and process identity

**STATUS: NEEDS MORE WORK**

- Finding: Privileged actions lacked a consistent execution boundary and process termination could lose exact target identity across UAC delay.
- Root cause: Earlier paths launched privileged commands directly or transported request data through an unauthenticated command-line channel.
- Files changed: `Sentinel.PrivilegedBroker`, `PrivilegedBrokerClient.cs`, process/quarantine integration.
- Security behavior before: Privileged request intent could be separated from the exact caller/target identity; process PID reuse and broad child termination could create unintended impact.
- Security behavior after: The hardening branch now uses a versioned allowlisted named-pipe broker protocol, `CurrentUserOnly` pipe creation, named-pipe peer PID verification, sibling Sentinel application executable verification, and execution-time process start/path/hash validation for termination. Cancellation/timeout paths do not report success and terminate the launched broker child where possible.
- Tests added: Build coverage exists; dedicated authenticated-IPC/UAC adversarial tests are still required.
- Tests passed: Desktop and broker x64 Release builds pass in Windows CI.
- Tests failed: No dedicated broker security runtime harness has yet established spoof resistance, UAC deny behavior, packaged executable identity, or all ACL semantics.
- Remaining concerns: Windows runtime validation for unauthorized same-user callers, package install path assumptions, UAC accept/deny, broker signing/provenance, ACL behavior, cancellation races, and target changes during approval.
- Related findings: SAI-A02, SAI-A03, SAI-A04, SAI-A17.
- Recommended next action: Add broker IPC adversarial runtime fixtures and validate from the packaged Store-style build before closing A07/A15.

### SAI-A09 / SAI-A17 — bounded subprocess ownership

**STATUS: NEEDS MORE WORK**

- Finding: Blocking output reads could bypass timeouts, and caller cancellation could leave child processes running.
- Root cause: Multiple helpers drained output synchronously before enforcing wall-clock timeout and lacked a common child-ownership policy.
- Files changed: `BoundedProcessRunner.cs`, driver evidence/research paths, additional command helpers, privileged broker client.
- Security behavior before: A verbose or hung child could stall monitoring indefinitely; cancellation could abandon a running child.
- Security behavior after: The common runner drains stdout/stderr concurrently, enforces wall-clock timeout, caps captured output, returns structured outcomes, and attempts process-tree termination on timeout/cancellation. Driver research now uses this runner rather than blocking `ReadToEnd()` paths.
- Tests added: `Sentinel.BoundedProcessRunnerAcceptanceHarness` covers normal, nonzero, heavy simultaneous output, timeout, cancellation, and output truncation.
- Tests passed: Pending current Windows CI run.
- Tests failed: None recorded yet for the new harness.
- Remaining concerns: Process-tree termination must be confirmed under real descendant processes and access-denied termination cases; remaining subprocess helpers must be re-audited for direct blocking patterns.
- Related findings: SAI-A13 and availability/stability release gates.
- Recommended next action: Record current harness result, then search/re-review all process-launch helpers before closure.

### SAI-A14 — temporary cleanup path race

**STATUS: NEEDS MORE WORK**

Automatic temporary-file cleanup currently fails closed and performs no deletion because a handle-based, reparse-resistant deletion primitive has not yet been validated. This removes the unsafe destructive behavior from the current branch, but the optimization capability is intentionally incomplete and A14 remains open.

### SAI-A16 — service restart dependency safety

**STATUS: NEEDS MORE WORK**

Automatic service restart currently fails closed rather than stopping a service through the old unsafe path. Dependency-aware broker execution, rollback, cancellation recovery, and final running-state verification are still required before this capability can be restored and A16 closed.

### SAI-A19 — final Ask Sentinel claim boundary

**STATUS: NEEDS MORE WORK**

Lexical phrase matching is no longer treated as authorization for security claims, and cloud model prose is kept advisory rather than copied directly into security-state claims. However, `MainWindow` can still replace a response after the orchestrator's validation step for optimization, external-investigation, and driver-answer paths. The final displayed response therefore needs a deterministic final validation/provenance boundary after all composition/replacement before A19 can close.

### SAI-A22 — bounded external/archive work

**STATUS: NEEDS MORE WORK**

Source now adds bounded response/archive/XML handling and routes driver catalog expansion through bounded process execution. Closure still requires malformed/oversized archive, decompression/file-count, redirect/source-policy, timeout, and resource-exhaustion tests.

### SAI-A24 / A25 / A27 / A28 — correctness and persistence hardening

**STATUS: NEEDS MORE WORK**

Source remediation now includes per-adapter throughput baselines/reset handling, bounded investigation-history retention/tail reads, benign-event filtering before aggregate error counting, and fail-closed maintenance persistence with durable pre-action reservation. These remain unchecked until their deterministic fault/concurrency/runtime tests are recorded.

## Release closure rules

After all 14 High findings have source corrections and their required validation, perform a fresh adversarial re-audit against every original High finding. After all 29 findings, perform a complete source re-audit. Before release, require commit-bound Windows build/runtime evidence, packaged Store evidence, backend configuration evidence, 1-hour and 8-hour stability artifacts, shipped-architecture validation, and known-limitations review.
