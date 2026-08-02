# SAI-031 — Application Update Plan

Version: 1.0

Status: Active

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Purpose

Define the production update model for Sentinel AI so installed systems can receive trusted releases without weakening Sentinel's security boundaries or disrupting user data.

# Update Requirements

- Updates must originate only from a Modern Methods controlled release channel.
- Every production update artifact must be code signed before distribution.
- Sentinel must never execute an update whose publisher/signature cannot be verified by Windows.
- Version progression must be explicit and monotonic; downgrade or rollback requires an intentional release decision.
- Updates must preserve user profile data, preferences, diagnostic history, and other data designated for retention.
- Update installation must not claim success until Windows reports successful package/application installation.
- Failed or interrupted updates must leave the previously installed working version recoverable whenever the Windows deployment mechanism supports it.
- Update checks must not delay the first visible Sentinel window or degrade monitoring startup performance.
- No private release credentials, signing secrets, or update-channel credentials may be stored in the repository.

# Release Model

Sentinel uses its Windows packaging/release boundary as the authoritative application deployment mechanism. Production releases will increment the package/application version, produce architecture-appropriate release artifacts, sign those artifacts under the approved publisher identity, and distribute them through the authorized Windows release channel selected for commercial deployment.

The application must not implement a custom executable downloader that bypasses Windows package/signature verification. Update availability may be surfaced to the user when appropriate, but installation authority remains governed by the trusted Windows deployment mechanism and the release channel.

# Data Preservation

Application binaries are replaceable release assets. User-specific state must remain outside replaceable application binaries and must not be deleted during a normal update. Uninstall behavior remains governed separately by SAI-028.

# Verification

Before Phase 7 item 9 can be marked complete:

1. Confirm release versioning is defined in the package/application release configuration.
2. Confirm update artifacts use the same trusted publisher identity required by SAI-029.
3. Confirm a normal Release build remains functional without release-channel credentials.
4. Confirm the update path preserves Sentinel user state.
5. Perform an installed-version-to-newer-version runtime update test once signed production release artifacts are available.
6. Verify Sentinel launches normally after update and monitoring, Technical Details, remediation safety boundaries, and Ask Sentinel remain functional.

---

End of Document
