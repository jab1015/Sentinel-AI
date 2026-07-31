# SAI-012 — Product Roadmap

**Version:** 1.7  
**Status:** Active  
**Last Updated:** 2026-07-31  
**Production Branch:** `main`

## Overall Progress

**Estimated product completion: 85%.**

Completed major foundations:

- Windows monitoring and security telemetry
- Investigation-first user experience
- Investigation Engine core — 18 of 18
- Evidence correlation and confidence foundations
- Safe Remediation Foundation — 10 of 10
- Process termination foundation
- Firewall blocking foundation
- Quarantine and restore foundation
- Remediation audit persistence
- Investigation history persistence
- Autonomous Protection core — 10 of 10
- Low-risk automatic remediation gating
- User-approval boundaries for moderate/high-risk remediation
- Evidence-confidence and execution-time revalidation safeguards
- Recurrence-aware escalation safeguards
- Startup and refresh responsiveness improvements
- First-refresh CPU sampling improvement
- Per-Windows-profile preferred-name onboarding
- Sustained memory-pressure investigation with application contributor context and actionable guidance

## Phase 1 — Monitoring Foundation

**Status: Complete**

CPU, memory, disk, network, process, service, Defender, Firewall, and Windows Event evidence are implemented and locally verified.

## Phase 2 — Investigation Experience

**Status: Complete**

Healthy-state presentation, progressive technical disclosure, plain-language conclusions, false-positive suppression, and exception-based user interruption are implemented.

## Phase 3 — Investigation Engine

**Status: Complete — 18 of 18**

Process, service, persistence, scheduled task, active connection, driver, firewall, WMI, ancestry, command-line, Windows Event, memory-pressure, and multi-signal investigation foundations are implemented.

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

**Status: Autonomous Protection core complete — 10 of 10; remaining integration active**

Completed:

1. Connected remediation recommendations to investigation decisions.
2. Defined low-risk actions Sentinel may perform automatically.
3. Preserved user approval requirements for moderate/high-risk actions.
4. Added autonomous execution isolation behind remediation policy.
5. Added evidence-confidence gating before automatic execution.
6. Added execution-time revalidation so stale decisions cannot trigger action.
7. Added safe security-state refresh and transient-operation retry handling.
8. Added verification-pending outcomes rather than claiming unverified success.
9. Added recurrence-aware investigation foundations and escalation safeguards.
10. Completed the Autonomous Protection core with defense-in-depth execution safeguards.

Remaining Phase 5 integration work:

1. Complete user-facing approval workflow for supported moderate-risk actions.
2. Complete quarantine management and safe restore presentation.
3. Add remediation/investigation history presentation without cluttering healthy state.
4. Expand network endpoint attribution and response.
5. Add background/minimized notification behavior for genuinely actionable findings.
6. Complete failure-path and remediation integration regression testing.
7. Continue startup/initial-investigation performance profiling and eliminate the intermittent lag observed in recent runtime verification.

## Phase 6 — Ask Sentinel / AI Assistance

**Status: Planned**

Natural-language questions grounded in current local evidence, investigation history, and verified system state, with explainable recommendations and no invented system state.

## Phase 7 — Production Hardening & Commercial Release

**Status: Planned / partially underway**

- Structured logging and diagnostics
- Fresh-clone build verification
- Release configuration verification
- Automated regression coverage
- Performance profiling and optimization
- One-hour and eight-hour stability tests
- Windows 10 and Windows 11 compatibility verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy, user, and troubleshooting documentation
- Accessibility and UX polish
- Release acceptance testing

## Progress Baseline

**85% is the synchronized overall product baseline as of 2026-07-31.** Future reported progress must use the roadmap and implementation tracker together. Progress must not move backward unless previously completed scope is explicitly reopened or proven incomplete and the reason is documented.

## Product Rule

Sentinel must investigate before acting, prefer silent monitoring when Windows can safely self-correct, request user involvement only when necessary, and verify every system-changing outcome before reporting success.
