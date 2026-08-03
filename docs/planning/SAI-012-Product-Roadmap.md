# SAI-012 — Product Roadmap

**Version:** 4.0  
**Status:** Active Development  
**Last Updated:** 2026-08-02  
**Production Branch:** `main`

## Overall Progress

Phases 1–7 are complete. Product scope is explicitly reopened because clean-machine testing confirmed that the original Sentinel product goal requires continuous intrusion and spyware-oriented protection that is not yet fully implemented.

**Current active phase: Phase 8 — Continuous Intrusion & Spyware Protection.**

No new installer will be treated as release-ready until Phase 8 and Windows automatic-start verification are complete.

## Completed Major Foundations

- Phases 1–5 complete
- Phase 6 Ask Sentinel / AI Assistance — 6 of 6 complete
- Phase 7 Production Hardening & Commercial Release — 12 of 12 complete
- Structured production diagnostic logging
- Investigation and safe-remediation foundations
- System tray/background operation foundation
- Installer/uninstaller packaging foundation
- Stability and compatibility verification foundation

## Phase 8 — Continuous Intrusion & Spyware Protection

**Status: Active — 0 of 8 complete**

1. [ ] Continuous network connection monitor for inbound/outbound TCP and relevant UDP activity, including process ownership and endpoint evidence.
2. [ ] Connection intelligence and anomaly classification that distinguishes ordinary traffic from meaningful intrusion indicators without alarming users about unfamiliar traffic alone.
3. [ ] Spyware/process behavior correlation using executable path, publisher/signature evidence, persistence, background behavior, process relationships, network behavior, and available Windows security evidence.
4. [ ] Safe response and containment integration using supported Windows Firewall/Defender mechanisms where evidence and policy permit; actions must be verified, logged, explainable, and preferably reversible.
5. [ ] Plain-English protection UX: only meaningful findings are surfaced; Sentinel states what happened, what Sentinel did, whether risk remains, and exactly what the user must do when assistance is required.
6. [ ] Reliable Windows sign-in startup, single-instance behavior, tray persistence, and continuous monitoring through normal window close, reboot, sleep/wake, and network reconnection.
7. [ ] Protection-health/self-monitoring so Sentinel can verify that its network monitor and required Windows protection layers are operating and can clearly report degraded protection.
8. [ ] Intrusion-protection acceptance testing covering benign traffic, suspicious simulations, listeners, unsigned/unknown test processes, VPN/VM scenarios, network loss/recovery, sleep/wake, reboot/startup, false positives, resource use, and long-duration operation.

## Product Acceptance Target

Sentinel must run continuously with Windows, monitor meaningful incoming and outgoing network activity, correlate connections to responsible processes, investigate suspicious intrusion/spyware indicators, take safe verified action when permitted, and disturb the user only when a meaningful security condition exists or user assistance is required.

Sentinel may use Microsoft Defender and Windows Firewall as trusted protection/enforcement layers; Sentinel is responsible for the intelligence, correlation, investigation, orchestration, verification, and user-facing explanation. Sentinel must not claim that every intrusion or spyware program can be detected.

## Release Gate

Do not create or approve another production installer merely because packaging succeeds. Release packaging resumes only after Phase 8 is complete and clean-machine verification demonstrates continuous protection and automatic Windows startup.

## Product Rule

Sentinel must investigate before acting, keep healthy users undisturbed, verify system-changing outcomes, keep Ask Sentinel grounded in verified evidence, continuously protect while running in the background, and clearly distinguish verified threats from ordinary Windows/network activity.
