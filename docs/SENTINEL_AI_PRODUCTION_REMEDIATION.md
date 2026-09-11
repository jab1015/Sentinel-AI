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

## Finding records

### SAI-A01 — Authenticode executable trust

**STATUS: NEEDS MORE WORK**

- Finding: Executable trust did not verify Authenticode integrity.
- Root cause: Certificate extraction and certificate-chain checks were treated as equivalent to full-file Authenticode verification; publisher substring matching could influence trust; cache identity was not bound to file content.
- Source changes: Added `WinVerifyTrust`-based verification, structured trust states, exact recognized-publisher comparison, content hashing before/after verification, and SHA-256-bound process signature cache behavior.
- Security behavior before: A signed-looking or publisher-lookalike binary could avoid the intended warning without Windows establishing full-file Authenticode trust.
- Security behavior after: Process trust decisions require Windows Authenticode verification; changed-during-verification files fail closed; unsigned is treated as a signal rather than malware proof.
- Tests added: Authenticode acceptance harness covers trusted Windows binary, unsigned/untrusted binary, tampered signed copy, and status mapping.
- Tests passed: Not established in this environment.
- Tests failed: None executed here; .NET SDK and Windows runtime unavailable.
- Remaining concerns: Windows validation of catalog-signed files, self-signed publisher lookalikes, timestamp-preserving replacement, revocation/network behavior, x86/x64/ARM64 native behavior.
- Related findings discovered: None beyond original A01 scope.
- Recommended next action: Run the Authenticode harness and adversarial fixture matrix in the Windows release-validation environment.

### SAI-A05 — AI gateway authentication and entitlement enforcement

**STATUS: NEEDS MORE WORK — BLOCKED — EXTERNAL VALIDATION REQUIRED**

- Finding: The AI gateway accepted direct `/v1/analyze` calls without authenticated identity or server-side paid entitlement enforcement.
- Root cause: Microsoft Store licensing was checked only in the desktop client; the gateway trusted caller-selected model tier and relied primarily on IP rate limiting.
- Source changes: Added short-lived HMAC-signed gateway sessions, Basic-only free sessions, server-side Microsoft Store entitlement exchange for Advanced sessions, Microsoft Entra service-token acquisition, Store `publisherQuery` entitlement validation, request replay IDs, server-side tier enforcement, provider concurrency limiting, and privacy-safe report logging.
- Desktop changes: Added Store Collections ID exchange using `StoreContext.GetCustomerCollectionsIdAsync`; Advanced AI now requires a gateway session created from a server-verified Store entitlement; Basic Ask Sentinel uses a Basic session.
- Security behavior before: A caller that could reach the endpoint could bypass the desktop subscription check and request Advanced provider usage.
- Security behavior after: `/v1/analyze` requires a signed short-lived gateway session; Basic sessions cannot request Advanced; paid session issuance requires a Store entitlement response for an allowlisted Sentinel product ID; provider secrets remain server-side.
- Tests added: Gateway security test harness still required before closure.
- Tests passed: Not established in this environment.
- Tests failed: None executed here; .NET SDK unavailable.
- Remaining concerns: Google Cloud deployment, Secret Manager configuration, Microsoft Entra/Partner Center association, distributed per-account/spend limits across Cloud Run instances, live Store entitlement response shape, session-key rotation, and staging replay/expiry tests.
- External validation required:
  - Associate the production Microsoft Entra application with the Sentinel Microsoft Store/Partner Center product.
  - Configure `SENTINEL_STORE_TENANT_ID`, `SENTINEL_STORE_CLIENT_ID`, and `SENTINEL_STORE_CLIENT_SECRET` from Secret Manager, never source control.
  - Configure `SENTINEL_GATEWAY_SESSION_SIGNING_KEY` with at least 32 bytes of cryptographically random secret material from Secret Manager.
  - Confirm paid Store product IDs in `SENTINEL_STORE_PAID_PRODUCT_IDS`; current source fallback includes monthly Store ID `9N67THV2Z1GP`.
  - Validate anonymous/tampered/expired/Basic-to-Advanced/replayed/unsubscribed requests in staging without provider usage.
  - Verify provider billing quota/budget alerts and distributed rate/spend controls.
- Related findings: SAI-A19, SAI-A20, SAI-A21, SAI-A22.
- Recommended next action: Add deterministic gateway-auth tests, deploy to staging with stub provider, then validate real Store entitlement flow.

### SAI-A15 / SAI-A07 — privileged execution boundary and process identity

**STATUS: NEEDS MORE WORK**

A new privileged broker direction exists on this hardening branch, but current review found that its command-line request channel is not an authenticated IPC boundary. A same-user process can construct an allowlisted request and launch the broker, after which user UAC consent could authorize that request. Do not treat this as a completed privilege boundary. Replace the command-line request transport with authenticated, ACL-constrained IPC and bind operations to the validated caller/request identity before closing A15/A07.

## Release closure rules

After all 14 High findings have source corrections and their required validation, perform a fresh adversarial re-audit against every original High finding. After all 29 findings, perform a complete source re-audit. Before release, require commit-bound Windows build/runtime evidence, packaged Store evidence, backend configuration evidence, 1-hour and 8-hour stability artifacts, and known-limitations review.
