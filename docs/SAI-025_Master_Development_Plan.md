# SAI-025 — Master Development Plan

Version: 1.5

Status: Active

Last Updated: 2026-07-31

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document is the authoritative master engineering plan for Sentinel AI.

# Project Mission

Develop a production-quality Windows investigation and security application combining native Windows monitoring, evidence correlation, explainable intelligence, safe remediation, autonomous low-risk protection, actionable recommendations, and a calm non-technical user experience.

# Current Status

Estimated overall product completion: **90%**.

Completed:

- Phase 1 — Monitoring Foundation
- Phase 2 — Investigation Experience
- Phase 3 — Investigation Engine: 18 of 18
- Phase 4 — Safe Remediation Foundation: 10 of 10
- Phase 5 — Remediation Integration & Autonomous Protection: 7 of 7 remaining-integration items complete; Autonomous Protection core 10 of 10 complete
- User-facing approval workflow and execution-time approval revalidation
- Quarantine/restore foundations and presentation integration
- Investigation/remediation history presentation
- Network attribution/response integration
- Background actionable attention signaling
- Failure-path and remediation regression safeguards
- Startup/load responsiveness improved and locally accepted
- Per-Windows-profile preferred-name onboarding
- Sustained memory-pressure investigation with application contributor context
- Grounded local Ask Sentinel evidence context
- Natural-language Ask Sentinel interaction surface

Current milestone:

**Phase 6 — Ask Sentinel / AI Assistance: 2 of 6 complete.**

Completed Phase 6 work:

1. Grounded local evidence context layer. Ask Sentinel must answer from verified local system evidence and explicitly acknowledge when evidence is insufficient.
2. Natural-language Ask Sentinel interaction surface. Questions refresh current Sentinel evidence before answering; supported current-system questions use only verified snapshot data and unsupported questions fail closed.

Remaining Phase 6 work:

3. Evidence-grounded response orchestration.
4. Investigation-history-aware explanations.
5. Explainable recommendations with strict no-invention safeguards.
6. Integration, failure-path, and runtime verification.

Planned after Phase 6:

- Phase 7 — Production Hardening & Commercial Release
- Structured diagnostics and logging
- Fresh-clone/release configuration verification
- Automated regression coverage
- Long-duration stability testing
- Windows 10/11 compatibility verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy/user/troubleshooting documentation
- Accessibility/UX polish
- Release acceptance testing

# Development Phases

## Phase 1 — Monitoring Foundation
Status: **Complete**

## Phase 2 — Investigation Experience
Status: **Complete**

## Phase 3 — Investigation Engine
Status: **Complete — 18 of 18**

## Phase 4 — Safe Remediation Foundation
Status: **Complete — 10 of 10**

## Phase 5 — Remediation Integration & Autonomous Protection
Status: **Complete**

Autonomous Protection core: **10 of 10 complete**. Remaining integration milestone: **7 of 7 complete**.

## Phase 6 — Ask Sentinel / AI Assistance
Status: **Active — 2 of 6 complete**

Ask Sentinel is being built as a grounded assistance layer over verified Sentinel evidence. It must not invent system state, threats, causes, remediation outcomes, or historical facts.

## Phase 7 — Production Hardening & Commercial Release
Status: **Planned / partially underway**

# Current Performance Note

Recent local verification confirms successful builds and acceptable startup behavior. The responsive shell-first startup and brief analysis state are accepted for the current development stage. Performance remains subject to Phase 7 hardening without reopening Phase 5.

# Progress Governance

**90% is the synchronized overall project baseline as of 2026-07-31.** SAI-012 Product Roadmap, SAI-013 Implementation Tracker, SAI-025 Master Development Plan, and README must report the same baseline and phase state.

Progress must not move backward unless previously completed functionality is explicitly reopened, removed, or proven incomplete, with the reason documented.

# Engineering Priorities

1. Preserve investigation-before-action behavior and remediation safety boundaries.
2. Keep healthy users undisturbed.
3. Ground Ask Sentinel in verified evidence.
4. Verify system-changing outcomes before reporting success.
5. Maintain synchronized documentation and a buildable application.

# Definition of Success

A successful release builds without errors, runs successfully, meets acceptance criteria, preserves remediation safety boundaries, never reports unverified remediation as successful, and never presents unsupported AI claims as verified system facts.

---

End of Document