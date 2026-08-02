# SAI-029 — Code Signing Plan

Version: 1.0

Status: Active

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Purpose

Define the production code-signing requirements for Sentinel AI release artifacts without committing private signing material to source control.

# Requirements

- Release packages must be signed before public distribution.
- Signing must use a trusted production certificate whose subject matches the package Publisher identity used for the commercial release.
- Private keys, certificate passwords, PFX files, hardware-token secrets, and signing-service credentials must never be committed to this repository.
- Repository configuration must remain buildable without access to production signing secrets.
- Unsigned local development builds must remain supported until production signing credentials are supplied through an authorized release environment.
- Signing must not alter Sentinel security, monitoring, remediation, persistence, or startup behavior.
- Signed release artifacts must be timestamped using the certificate/provider's supported trusted timestamp service so signatures remain verifiable after certificate expiration where applicable.

# Release Signing Boundary

The packaging project intentionally keeps `AppxPackageSigningEnabled` disabled in source-controlled defaults. Production signing is a release operation performed only when authorized certificate material is available. This prevents developer builds from depending on machine-specific certificates and prevents secret material from entering source control.

# Verification

Before Phase 7 item 8 can be marked complete:

1. Confirm the repository contains no private signing material.
2. Confirm normal Release builds continue to succeed with signing disabled by default.
3. Confirm the production signing procedure identifies the certificate by secure release-environment configuration rather than a repository secret.
4. When the production certificate is available, produce a signed release artifact and verify its Authenticode/package signature and publisher identity on Windows.

---

End of Document
