# SAI-005 — Product Roadmap

Version: 2.0  
Status: Active — Production hardening roadmap  
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

Status: **ACTIVE**

Baseline: 29 findings (14 High, 15 Medium) from commit `1218f5d...`.

Current accomplishments include bounded subprocess execution, quarantine recovery/cleanup hardening, package payload verification, broker package-identity binding, Authenticode improvements, deterministic DISM/SFC classification, redaction/history/diagnostic/event-filtering tests, and AI gateway security harness coverage.

Exit criteria:

- Every High and Medium finding corrected or explicitly disabled/fail-closed where a safe capability is not ready.
- Required deterministic, adversarial, packaged-runtime, cloud, Store, and architecture-specific evidence attached to each finding.
- Full 29-finding adversarial re-audit completed.

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

Status: **ACTIVE**

- Preserve natural-language investigation and explanations.
- Final displayed answer must pass deterministic claim/provenance validation after every response replacement/composition path.
- Separate VERIFIED FACT, OBSERVED, INFERRED, ACTION VERIFIED, and ADVISORY semantics.
- AI prose must never invent blocked/quarantined/repaired/Defender/firewall outcomes.

## Phase D — Cloud and Entitlement Validation

Status: **BLOCKED ON EXTERNAL VALIDATION**

- Deploy/validate Google Cloud gateway configuration.
- IAM + Secret Manager + provider secret isolation.
- Store entitlement validation for paid tier.
- Replay/rate/concurrency/spend controls across multiple instances.
- Abuse, timeout, outage, restart, and secret-unavailable tests.

## Phase E — Release Qualification

Status: **PLANNED AFTER HARDENING**

Required before release sign-off:

- Store-signed/package provenance validation.
- Clean install, upgrade, uninstall.
- x64 plus every actually shipped architecture.
- Supported Windows versions.
- Standard/admin/UAC matrices.
- Startup/background/Explorer restart/sleep-wake/network-loss behavior.
- Defender/firewall interactions.
- Crash/recovery/disk-full/access-denied tests.
- Fresh 1-hour and 8-hour stability/resource runs on the final commit.
- Independent final security review.

## Current Next Milestones

1. Broker packaged/UAC adversarial validation (A07/A15).
2. Final Ask Sentinel claim boundary (A19).
3. Authenticode extended runtime matrix (A01).
4. AI gateway staging/Store validation (A05).
5. Remaining findings and full re-audit.
6. Final release qualification.

---

End of Document
