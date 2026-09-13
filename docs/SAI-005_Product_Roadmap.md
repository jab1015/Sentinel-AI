# SAI-005 — Product Roadmap

Version: 2.8  
Status: Active — Source/automated hardening qualified; physical validation pending  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Product Vision

Sentinel AI will be a trustworthy Windows security and system-assistance platform that continuously monitors verified evidence, explains findings in plain language, detects suspicious behavior, safely contains/remediates supported threats, preserves user control, and never claims an action succeeded without verification.

## Established Product Foundation

Complete or substantially implemented:

- WinUI 3/.NET 8 desktop application.
- Hardware/software/system monitoring.
- Defender and Firewall evidence collection.
- Ask Sentinel investigation/explanation experience.
- Activity/investigation history and diagnostics.
- Optimization/repair assistance with approval and verification concepts.
- Microsoft Store/MSIX packaging foundation.
- Privileged broker architecture.
- Quarantine architecture.
- AI gateway architecture with server-side session/tier controls in source.

## Phase A — Production Security Hardening

Status: **SOURCE/AUTOMATED QUALIFIED — PHYSICAL/EXTERNAL VALIDATION ACTIVE**

Baseline: 29 findings (14 High, 15 Medium) from commit `1218f5d39e2e98f955179d7911b013636068d373`.

Current fully qualified production-source/test checkpoint: `cff46692d1260349eae531632170fb687deed36f`.

All 12 required hardening workflows are green at that exact checkpoint:

- Security Hardening Windows `34718588297`
- Security Hardening Package `34718588319`
- Architecture Hardening `34718588324`
- Package Architecture Hardening `34718588318`
- Subprocess Boundary Audit `34718588329`
- Driver Repair Hardening `34718588291`
- External Research Hardening `34718588312`
- Child Process Safety Hardening `34718588299`
- Investigation Cache Hardening `34718588302`
- Network Throughput Hardening `34718588295`
- Optimization State Hardening `34718588301`
- A14 Temporary Cleanup Hardening `34718588338`

The formerly CI-gated A09, A17, A21, A22, and A24 are now source-complete. A01-A04, A06-A28 are source-complete where applicable and remain subject to their installed/runtime matrices. A05 remains blocked on Google Cloud + Microsoft Store/Partner Center validation. A29 remains blocked on signed package and real architecture/runtime qualification.

Source/automated qualification is not production readiness.

Remaining Phase A work is physical/external qualification:

1. Installed broker/UAC adversarial testing for A07/A15.
2. Extended Authenticode runtime fixture matrix for A01.
3. Installed quarantine/crash/recovery testing for A02/A03/A04.
4. Real Defender/firewall, driver/device, temp cleanup, service restart, Ask Sentinel UI, network churn, optimization-state, startup/lifecycle, install/update/uninstall and sleep/wake testing.
5. Google Cloud + Store entitlement staging for A05, including multi-instance replay/rate state.
6. Signed Store-style package qualification and real x86/x64/ARM64 runtime for every architecture intended to ship.
7. Fresh final-commit 1-hour and 8-hour stability/resource runs.
8. Final adversarial re-audit of all High findings and then all 29 findings, including remediation-introduced vulnerabilities.

Exit condition: `READY FOR FINAL INDEPENDENT REVIEW`. Do not merge automatically.

## Phase B — Active Protection

Status: **IN DEVELOPMENT / NOT YET RELEASE-QUALIFIED**

Objectives:

- Detect suspicious/malicious behavior and potentially tainted files.
- Ransomware/malware indicators with honest confidence/evidence semantics.
- User warning + explanation + safe blocking/containment where technically supported.
- Protected file quarantine with restore/permanent-delete workflows.
- Suspicious network containment with explicit unblock/release controls where supported.
- Strong Defender integration without overstating Sentinel's independent blocking coverage.
- Exact-target elevated remediation through the broker.

## Phase C — Ask Sentinel Trust Boundary

Status: **SOURCE IMPLEMENTED — RUNTIME VALIDATION REMAINS**

- Final displayed answers pass deterministic validation after response replacement/composition.
- Advisory/inferred prose cannot assert blocked/quarantined/repaired/Defender/firewall outcomes without verified action evidence.
- Installed/runtime response paths and provenance UX still require validation.

## Phase D — Cloud and Entitlement Validation

Status: **BLOCKED ON EXTERNAL VALIDATION**

- Deploy/validate Google Cloud gateway configuration.
- IAM + Secret Manager + provider secret isolation.
- Store entitlement validation for paid tier.
- Distributed replay/rate/concurrency/spend controls across multiple instances.
- Abuse, timeout, outage, restart, and secret-unavailable tests.

## Phase E — Release Qualification

Status: **ACTIVE AFTER SOURCE QUALIFICATION**

Required before release sign-off:

- Store-signed/package provenance validation.
- Clean install, upgrade, uninstall.
- x86/x64/ARM64 runtime for every architecture actually shipped.
- Supported Windows versions.
- Standard-user/admin/UAC matrices.
- Startup/background/Explorer restart/sleep-wake/network-loss behavior.
- Defender/firewall interactions.
- Crash/recovery/disk-full/access-denied tests.
- Fresh 1-hour and 8-hour stability/resource runs on the final commit.
- Independent final security review.

## Phase F — Premium Privacy Protection

Status: **AUTHORIZED — ACTIVE ON ISOLATED BRANCH `feature/premium-privacy-foundation`**

Premium Privacy development is intentionally isolated from the hardening qualification branch. It must not be merged into this branch until the hardening baseline reaches `READY FOR FINAL INDEPENDENT REVIEW` or explicit earlier authorization is given.

Planned/active scope:

- File Explorer integration: Inspect with Sentinel AI, later Encrypt File, Add to Sentinel Vault, Secure Delete.
- AES-256-GCM authenticated file encryption with fresh per-file DEKs, unique nonce material, authenticated metadata, versioned format, and bounded chunked streaming.
- Windows current-user protection, portable password protection, and independent recovery keys.
- Sentinel Vault using a VMK -> wrapped per-item DEK hierarchy.
- Storage-aware Secure Delete with exact-object revalidation, protected/reparse/link/race defenses, durable transactions, crash recovery, and honest VERIFIED / REQUESTED / REMAINS / CANNOT PROVE semantics.
- Broad attributable-copy/history discovery across supported Windows/provider sources.
- Server-authoritative premium entitlement.

Safety boundaries remain mandatory:

- no unrestricted privileged `DeletePath(string)` primitive;
- discovery and deletion are separate operations;
- fuzzy filename similarity alone never authorizes deletion;
- normal single-file Secure Delete must not directly manipulate `pagefile.sys`, `swapfile.sys`, or `hiberfil.sys` or silently wipe unrelated restore/history/system data;
- do not claim guaranteed forensic irrecoverability where storage/media behavior cannot prove it;
- encryption must verify the separate encrypted output before any optional plaintext removal.

Authoritative implementation status for Phase F lives on `feature/premium-privacy-foundation` and in `docs/privacy/` on that branch.

## Current Next Milestones

### Hardening branch

1. Execute the installed Windows/UAC/adversarial runtime program.
2. Execute A05 Google Cloud + Microsoft Store entitlement staging.
3. Complete signed Store package and real architecture qualification.
4. Run final 1-hour/8-hour stability tests.
5. Re-audit all High findings and all 29 findings.
6. Stop at `READY FOR FINAL INDEPENDENT REVIEW`; do not merge automatically.

### Premium Privacy branch

Continue source/test development independently, then integrate only after the hardening gate permits it.

---

End of Document
