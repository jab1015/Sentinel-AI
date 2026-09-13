# SAI-PRIV-006 — Premium Privacy Entitlement

## Status

SOURCE IMPLEMENTED / CI VERIFIED / STAGING NOT VERIFIED / WINDOWS RUNTIME REQUIRED

Source/test checkpoint: `75edc968d243ccda8da84ea05d687d881270f28f`  
Premium Privacy workflow: `34734415429` — **SUCCESS**

This document defines the subscription boundary for Premium Privacy. Microsoft Store remains the commercial authority. Sentinel's Google Cloud gateway is the server-authoritative enforcement point for premium capability issuance and execution revalidation. No UI boolean, Explorer handoff value, cached local Store status, or reusable client token is sufficient authorization.

## Premium scopes

The gateway recognizes only these exact one-operation capability scopes:

- `privacy.encrypt`
- `privacy.vault`
- `privacy.secure-delete`
- `privacy.discovery`

`Inspect with Sentinel AI` is not moved behind this Premium Privacy capability boundary by this design.

## Authoritative flow

1. Sentinel obtains a short-lived Microsoft Store collections identity through the existing StoreContext flow.
2. Sentinel requests an existing paid gateway session from `/v1/session/store`.
3. The gateway verifies the authoritative Microsoft Store collections entitlement for the configured Sentinel paid product ID (`9N67THV2Z1GP`, `sentinel-ai-monthly`).
4. Sentinel requests one exact privacy capability scope.
5. The gateway verifies that the Store identity matches the authenticated paid-session subject and re-queries Store entitlement before issuing the capability.
6. The capability is HMAC-authenticated server-side, feature-scoped, subject-bound, short-lived (45 seconds), and carries a unique token ID.
7. Sentinel submits the capability immediately before the local premium operation.
8. The gateway validates signature, lifetime, subject, exact feature scope, and one-time replay state, consumes the capability, then re-queries Microsoft Store again.
9. Only after that server-authoritative check succeeds does Sentinel perform the independent local filesystem/cryptographic safety validation required by the requested feature.

Entitlement never expands filesystem authority. A paid user still cannot Secure Delete a protected Windows object, directory, reparse target, multiply-linked file, Sentinel installation file, or any object whose exact identity cannot be retained and revalidated.

## Offline and unavailable policy

Premium creation and cleanup fail closed when the authoritative gateway/Store capability cannot be verified. Sentinel does not invent unlimited offline premium access.

Current source behavior for a new Premium Privacy action when network, Store, gateway session signing, or capability validation is unavailable is: deny the new premium operation and explain that verification is unavailable.

This policy does **not** convert subscription status into a lock on user-owned encrypted data.

## Recovery and decryption access policy

Existing encrypted files and existing Sentinel Vault content must retain a safe recovery/decryption path even if:

- a subscription expires or is revoked;
- Microsoft Store is temporarily unavailable;
- the gateway is temporarily unavailable;
- the device is offline.

Premium entitlement gates creation/management/cleanup features. It must not be inserted into the cryptographic key-unwrapping or user-data recovery path in a way that traps already-owned encrypted content.

The current Premium Privacy operations façade intentionally gates encryption creation, new Vault item creation, Secure Delete, and related discovery. The underlying decrypt/recovery services are not routed through that entitlement gate.

## Replay behavior

Privacy capability token IDs are one-time consumable in the current gateway source. A consumed token is rejected if presented again to the same gateway process. Wrong feature scope, wrong Store subject, malformed token, tampered signature, and expired token are rejected.

### Staging blocker — shared replay/rate state

The current privacy capability replay store is process-local. The gateway's existing request rate limiter and provider concurrency state are also process-local. Therefore a multi-instance Cloud Run deployment cannot yet claim cross-instance replay/rate enforcement.

This is an explicit staging qualification blocker under the Premium Privacy release rules. Until shared state is implemented and tested (for example, a transactional/atomic shared backend suitable for one-time capability consumption and cross-instance limits), staging may not be marked `STAGING VERIFIED` for multi-instance privacy entitlement behavior.

A single-instance deployment does not prove the required multi-instance property and must not be used to erase this blocker from the qualification record.

No Google Cloud deployment was performed from this repository session because no authenticated GCP deployment surface is available here, and the shared-state requirement would block a valid multi-instance staging claim even if a deployment command were available.

## Microsoft Store failure states

Expected authoritative behavior:

- active paid entitlement: capability may be issued if all other checks succeed;
- inactive/expired/revoked entitlement: deny premium capability/action;
- malformed identity/capability: deny;
- Store unavailable: return unavailable/fail closed for new premium operations;
- network unavailable: fail closed for new premium operations;
- entitlement changes while an operation is pending: final capability validation re-queries Store before local execution and must deny if the entitlement is no longer active.

Deterministic gateway acceptance at the CI checkpoint covers active, inactive, expired, revoked, unrelated-product, malformed Store response, malformed collections identity, Store outage, network outage, missing server credential, capability tamper, expiry, wrong scope/subject and replay.

Real Partner Center / installed StoreContext evidence is still required. Deterministic fixtures do not upgrade the release state to Store staging verified.

## Explorer boundary

The native Explorer extension is not an entitlement client. It does not talk to Microsoft Store or Google Cloud. Explorer emits only a bounded one-time action-intent handoff containing the selected filesystem path(s), a timestamp, and one of the allowed command names. Sentinel reopens/revalidates the selected object, presents application UX, verifies entitlement server-side, and performs all cryptographic/filesystem work inside the app/service boundary.

A forged same-user handoff therefore cannot carry an entitlement token, broker authority, cryptographic key, or delete authorization.

## Qualification ladder

- **DESIGN** — complete for the flow described above.
- **SOURCE IMPLEMENTED** — implemented for scoped gateway capabilities and application-side authoritative checks.
- **CI VERIFIED** — **YES** at `75edc968d243ccda8da84ea05d687d881270f28f`, run `34734415429` (**SUCCESS**).
- **STAGING VERIFIED** — **NO** until Google Cloud staging is deployed with required secrets/least privilege and shared multi-instance replay/rate behavior is implemented and validated.
- **WINDOWS RUNTIME REQUIRED** — **YES** for StoreContext/Partner Center test-account behavior and installed Explorer/application flows.
- **FULLY QUALIFIED** — **NO** until staging and the later physical Windows privacy validation campaign are complete.

See `SAI-PRIV-007_PreWindows_Adversarial_Review.md` for the source review and blocker classification.
