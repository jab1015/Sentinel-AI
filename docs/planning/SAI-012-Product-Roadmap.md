# SAI-012 — Product Roadmap

**Version:** 1.5  
**Status:** Active  
**Last Updated:** 2026-07-31  
**Production Branch:** `main`

## Overall Progress

**Estimated product completion: 72%.**

Completed major foundations:

- Windows monitoring and security telemetry
- Investigation-first user experience
- Investigation Engine core
- Evidence correlation and confidence foundations
- Safe remediation policy
- Process termination foundation
- Firewall blocking foundation
- Quarantine and restore foundation
- Remediation audit persistence
- Investigation history persistence
- Startup and refresh responsiveness improvements

## Phase 1 — Monitoring Foundation

**Status: Complete**

CPU, memory, disk, network, process, service, Defender, Firewall, and Windows Event evidence are implemented and locally verified.

## Phase 2 — Investigation Experience

**Status: Complete**

Healthy-state presentation, progressive technical disclosure, plain-language conclusions, false-positive suppression, and exception-based user interruption are implemented.

## Phase 3 — Investigation Engine

**Status: Complete — 18 of 18**

Process, service, persistence, scheduled task, active connection, driver, firewall, WMI, ancestry, command-line, and multi-signal investigation foundations are implemented.

## Phase 4 — Safe Remediation Foundation

**Status: Complete — 10 of 10**

Implemented:

- Central remediation policy
- Evidence requirements before system changes
- User approval gating
- Windows protected-component safeguards
- Verified process termination service
- Verified outbound firewall blocking service
- File quarantine
- Hash-verified restore
- Remediation audit history
- Investigation history and recurrence counting
- Transient Windows Update 0x80073D02 suppression
- Reduced investigation refresh cadence and deferred initial investigation for improved responsiveness

## Phase 5 — Remediation Integration & Autonomous Protection

**Status: Next**

Planned sequence:

1. Connect remediation services to Investigation Engine decisions.
2. Define low-risk actions Sentinel may perform automatically.
3. Add user approval workflow for moderate-risk actions.
4. Add post-remediation verification and outcome presentation.
5. Use investigation history for recurrence-aware escalation.
6. Integrate quarantine management and safe restore workflow.
7. Add useful remediation/investigation history presentation without cluttering the healthy experience.
8. Expand network endpoint attribution and response.
9. Add background/minimized notification behavior for genuinely actionable findings.
10. Complete remediation integration and failure-path regression testing.

## Phase 6 — Ask Sentinel / AI Assistance

**Status: Planned**

Natural-language questions grounded in current local evidence, investigation history, and verified system state.

## Phase 7 — Production Hardening & Commercial Release

**Status: Planned / partially underway**

- Structured logging and diagnostics
- Fresh-clone build verification
- Release configuration verification
- Performance profiling
- One-hour and eight-hour stability tests
- Windows 10 and Windows 11 compatibility verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy and user documentation
- Accessibility and UX polish
- Release acceptance testing

## Product Rule

Sentinel must investigate before acting, prefer silent monitoring when Windows can safely self-correct, request user involvement only when necessary, and verify every system-changing outcome before reporting success.
