# SAI-025 — Master Development Plan

Version: 4.0

Status: Active Development

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Current Status

Phases 1–7 are complete. Product completion has been explicitly reopened after clean-machine/VM testing demonstrated that the original Sentinel acceptance target requires continuous intrusion and spyware-oriented monitoring plus reliable Windows automatic startup.

Active work: **Phase 8 — Continuous Intrusion & Spyware Protection: 0 of 8 complete.**

Current milestone: **8.1 — Continuous Network Connection Monitor.**

No new release installer is to be created or approved until Phase 8 acceptance is complete.

Completed:

- Phase 1 — Monitoring Foundation
- Phase 2 — Investigation Experience
- Phase 3 — Investigation Engine: 18 of 18
- Phase 4 — Safe Remediation Foundation: 10 of 10
- Phase 5 — Remediation Integration & Autonomous Protection
- Phase 6 — Ask Sentinel / AI Assistance: 6 of 6
- Phase 7 — Production Hardening & Commercial Release: 12 of 12

# Phase 8 — Continuous Intrusion & Spyware Protection

1. [ ] Continuous network connection monitor for inbound/outbound TCP and relevant UDP activity with process, executable, local endpoint, remote endpoint, port/protocol, state, and available trust evidence.
2. [ ] Connection intelligence/anomaly classification using corroborated evidence and false-positive controls; unfamiliar traffic alone is never sufficient for a threat claim.
3. [ ] Spyware/process behavior correlation across executable trust/location, persistence, parent/child relationships, unexpected background behavior, network behavior, and available Defender/security evidence.
4. [ ] Safe response and containment using supported Windows Firewall/Defender mechanisms where appropriate, with verification, audit logging, clear rationale, and reversible actions where feasible.
5. [ ] Plain-English protection experience that surfaces only meaningful conditions and tells the user what happened, what Sentinel did, whether risk remains, and exact required steps when Sentinel needs assistance.
6. [ ] Reliable Windows sign-in startup and continuous background operation: single instance, tray persistence, window-close behavior, reboot, sleep/wake, and network reconnection.
7. [ ] Protection health/self-monitoring that verifies Sentinel's network monitor and required Windows protection layers remain operational and reports degraded protection accurately.
8. [ ] Acceptance testing for benign/suspicious traffic, listeners, unsigned/unknown test processes, VPN/VM scenarios, network interruption/recovery, sleep/wake, reboot/startup, false positives, resource use, and long-duration operation.

# Original Product Acceptance Target

Sentinel AI is not complete merely because it can inspect system state, display Windows security status, package successfully, or react to historical Windows events. Sentinel must operate continuously and provide an intelligent protective layer focused on meaningful intrusion and spyware indicators.

Sentinel may rely on Microsoft Defender and Windows Firewall as mature antivirus/firewall enforcement layers. Sentinel's responsibility is continuous observation, evidence correlation, investigation, decision support, safe orchestration, verification, and clear user communication. Sentinel must not claim universal detection of every intrusion or spyware technique.

# Release Gate

Installer work is paused. Packaging already demonstrated that Sentinel can be packaged and installed, but the next production installer must not be generated or approved until Phase 8 is complete and clean-machine verification confirms continuous protection and automatic Windows startup.

# Progress Governance

The completed Phase 1–7 history remains valid and must not be erased. However, the previous 100% product-complete statement is superseded because required original product scope was absent from the documented plan. Until Phase 8 passes, report Phase 8 progress explicitly as `n of 8` and do not describe Sentinel as product-complete or release-ready.

# Definition of Success

A successful Sentinel release builds without errors, starts reliably with Windows, remains active in the tray, continuously monitors meaningful inbound/outbound activity, correlates suspicious activity to responsible processes when evidence permits, investigates spyware/intrusion indicators, safely handles verified conditions when authorized, clearly instructs the user when assistance is required, avoids alarming users about routine activity, verifies remediation outcomes, and preserves evidence-grounded AI behavior.

---

End of Document