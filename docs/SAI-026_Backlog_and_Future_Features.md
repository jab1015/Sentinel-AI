# SAI-026 — Product Backlog & Future Features

Version: 2.0  
Status: Active — Security hardening backlog  
Last Updated: 2026-09-12

Copyright (c) 2026 Modern Methods.

---

## Priority Definitions

- **P0** — Required before production-security sign-off.
- **P1** — Active-protection capability required for the intended product direction but may ship only when safely verified.
- **P2** — Significant product/UX improvements.
- **P3** — Longer-term/enterprise work.

## P0 — Production Security Closure

### Original assessment

- [ ] Close/fully verify all 14 High findings.
- [ ] Close/fully verify all 15 Medium findings.
- [ ] Re-audit all High findings.
- [ ] Re-audit all 29 findings.

### Quarantine

- [x] Deterministic recovery semantic guard.
- [x] Restore/delete cleanup failure preservation tests.
- [x] Forged/corrupt/tamper/collision/crash deterministic harness coverage.
- [ ] Standard-user ACL/owner/DACL proof.
- [ ] Reparse/junction/symlink/hardlink adversarial matrix.
- [ ] Source/destination race matrix.
- [ ] Disk-full/access-denied/orphan/all-checkpoint crash matrix.
- [ ] Installed broker/UAC quarantine tests.

### Privileged broker

- [x] Allowlisted protocol and exact-target checks.
- [x] Package-full-name client/broker binding.
- [x] Deterministic package identity policy harness.
- [ ] Unauthorized same-user/copy/spoof tests.
- [ ] Malformed/oversized/extra IPC tests.
- [ ] UAC accept/cancel/client-exit/broker-hang/disconnect tests.
- [ ] PID reuse/target replacement/pipe race tests.
- [ ] Installed package + upgrade lifecycle tests.

### Ask Sentinel

- [ ] Final display-time validator after all response replacements (A19).
- [ ] Provenance labels/claim regression tests.
- [ ] Ensure model text cannot invent blocked/quarantined/repaired/security-control outcomes.

### Authenticode

- [ ] Catalog/timestamp/revocation/offline fixture matrix.
- [ ] Self-signed/untrusted/lookalike cases.
- [ ] Replacement/cache-invalidation races.
- [ ] Shipped architectures.

### AI gateway

- [ ] Google Cloud staging deployment validation.
- [ ] IAM + Secret Manager.
- [ ] Store entitlement abuse matrix.
- [ ] Distributed replay/rate/concurrency/spend state.
- [ ] Provider timeout/outage/restart/secret-unavailable cases.

### Package/runtime assurance

- [x] Unsigned x64 generated-MSIX payload gate.
- [ ] Store-signed provenance.
- [ ] Clean install/upgrade/uninstall.
- [ ] Supported Windows versions/architectures.
- [ ] Standard/admin/UAC/startup/background matrices.
- [ ] Defender/firewall/sleep-wake/network-loss/recovery.
- [ ] Final 1-hour and 8-hour stability/resource runs.

## P1 — Active Protection

- Malware/ransomware behavior indicators.
- Suspicious file/process detection with honest confidence semantics.
- Safe blocking/containment where Windows integration proves enforcement.
- Network containment/unblock workflows where supported.
- Defender-integrated investigation/remediation.
- Protected quarantine UX: inspect, restore, permanent delete, recovery-required states.
- User warning/explanation around supported protective actions.

## P1 — Security Intelligence

- Event-driven evidence expansion.
- Startup/persistence/service/driver analysis.
- Better process lineage and signer reputation context.
- Network attribution improvements including short-lived/UDP/IPv6/QUIC limitations.
- Threat/risk correlation without overstating evidence.

## P2 — Reporting and UX

- Historical security reports.
- Export options where privacy-safe.
- Accessibility/localization.
- Performance/responsiveness improvements.
- User-visible recovery/containment explanations.

## P3 — Enterprise / Future Research

- Multi-device management and policy.
- Central reporting.
- ETW/event-driven telemetry research.
- SmartScreen/Windows Security integration research.
- TPM/Secure Boot posture.
- Extensibility/plugin architecture only after core security boundary is mature.

## Technical Debt Rule

Security correctness outranks feature restoration. A capability that cannot yet be made race-resistant, recoverable, and verifiable should remain explicitly disabled/fail-closed rather than ship with unsafe behavior.

---

End of Document
