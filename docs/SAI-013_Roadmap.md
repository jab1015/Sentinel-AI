# SAI-013 — Product Roadmap

Version: 2.0  
Status: Active  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Vision

Deliver a commercial-quality Windows security platform that combines verified native system evidence, understandable AI assistance, safe active protection, recoverable containment, and narrowly scoped privileged remediation.

## Phase 1 — Monitoring and Investigation Foundation

Status: **ESTABLISHED**

Includes native system monitoring, Defender/Firewall evidence, process/network/system evidence, investigation UX, history, diagnostics, Activity Center, optimization transparency, and Ask Sentinel.

## Phase 2 — Production Security Hardening

Status: **ACTIVE**

Assessment baseline: `1218f5d...`; 29 findings (14 High, 15 Medium).

Completed/source-hardened areas include:

- Common bounded subprocess runner and 10x CI reliability gate.
- Transactional protected quarantine/recovery semantics and adversarial CI.
- Fail-closed quarantine metadata cleanup.
- Privileged broker allowlist/identity checks and package-full-name binding.
- Unsigned x64 MSIX payload verification.
- Authenticode trust improvements.
- DISM/SFC deterministic classification.
- Redaction, event filtering, history, diagnostics, network process collection, and AI gateway security tests.

Exit criteria: all findings fully verified or safely disabled, High re-audit complete, all-29 re-audit complete.

## Phase 3 — Active Protection

Status: **PARTIALLY IMPLEMENTED; RUNTIME QUALIFICATION REQUIRED**

- Suspicious process/file behavior detection.
- Malware/ransomware indicators.
- Protected quarantine/restore/delete.
- Network containment/unblock where supported.
- Defender-aware protection and explanation.
- Exact-target elevated operations.
- User warning/explanation before or after supported blocking actions as appropriate.

Sentinel must not claim comprehensive antivirus/firewall replacement capability where Windows APIs/evidence do not prove it.

## Phase 4 — Trusted AI and Cloud Enforcement

Status: **ACTIVE / EXTERNAL VALIDATION BLOCKED**

- Final Ask Sentinel response claim/provenance boundary.
- Server-side authenticated sessions and tier enforcement.
- Microsoft Store entitlement validation.
- Google Cloud IAM/Secret Manager deployment.
- Distributed replay/rate/concurrency/spend controls.
- Deployed redaction/log inspection.

## Phase 5 — Release Qualification

Status: **NOT STARTED ON FINAL HARDENED COMMIT**

- Signed/Store package provenance.
- Install/upgrade/uninstall.
- Supported Windows + shipped architectures.
- UAC/standard/admin/startup/background matrices.
- Defender/firewall/sleep-wake/network-loss/recovery.
- Failure injection and crash recovery.
- 1-hour and 8-hour stability/resource runs.
- Independent final production/security review.

## Immediate Sequence

1. A07/A15 broker packaged/UAC adversarial validation.
2. A19 final Ask Sentinel display validation.
3. A01 Authenticode extended matrix.
4. A05 cloud/Store staging validation.
5. Remaining finding remediation.
6. High and all-29 re-audits.
7. Final release qualification.

---

End of Document
