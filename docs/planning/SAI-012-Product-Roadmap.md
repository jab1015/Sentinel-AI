# SAI-012 — Product Roadmap

**Version:** 2.2  
**Status:** Active  
**Last Updated:** 2026-07-31  
**Production Branch:** `main`

## Overall Progress

**Estimated product completion: 96%.**

## Completed Major Foundations

- Phase 1 — Monitoring Foundation
- Phase 2 — Investigation Experience
- Investigation Engine — 18 of 18
- Safe Remediation Foundation — 10 of 10
- Autonomous Protection core — 10 of 10
- Phase 5 remaining remediation integration — 7 of 7
- User-facing approval workflow
- Quarantine/restore integration
- Investigation/remediation history presentation
- Network attribution/response integration
- Background actionable attention signaling
- Failure-path/remediation regression safeguards
- Startup responsiveness improvements and accepted current load behavior
- Preferred-name onboarding per Windows profile
- Sustained memory-pressure investigation with application contributor context
- Grounded local Ask Sentinel evidence context
- Natural-language Ask Sentinel interaction surface
- Central evidence-grounded Ask Sentinel response orchestration
- Investigation-history-aware Ask Sentinel explanations
- Explainable safeguarded Ask Sentinel recommendations

## Phase 1 — Monitoring Foundation

**Status: Complete**

## Phase 2 — Investigation Experience

**Status: Complete**

## Phase 3 — Investigation Engine

**Status: Complete — 18 of 18**

## Phase 4 — Safe Remediation Foundation

**Status: Complete — 10 of 10**

## Phase 5 — Remediation Integration & Autonomous Protection

**Status: Complete**

Autonomous Protection core: **10 of 10 complete**. Remaining integration milestone: **7 of 7 complete**.

Phase 5 now includes approval gating and revalidation, quarantine/restore integration, history presentation, network response integration, background attention signaling, regression/fail-safe safeguards, and acceptable shell-first startup behavior.

## Phase 6 — Ask Sentinel / AI Assistance

**Status: Active — 5 of 6 complete**

1. [x] Grounded local evidence context layer.
2. [x] Natural-language Ask Sentinel interaction surface.
3. [x] Evidence-grounded response orchestration.
4. [x] Investigation-history-aware explanations.
5. [x] Explainable recommendations with strict no-invention safeguards.
6. [ ] Integration, failure-path, and runtime verification.

Recommendation questions now use verified guidance and remediation state, including approval requirements and autonomous-action eligibility. Ask Sentinel distinguishes a recommendation from an executed action and never reports remediation success unless the current verified snapshot records a successful outcome.

Ask Sentinel must use verified local evidence and verified persisted history and clearly state when available evidence is insufficient. It must never invent system state, causes, threats, history, remediation actions, or outcomes.

## Phase 7 — Production Hardening & Commercial Release

**Status: Planned / partially underway**

- Structured logging and diagnostics
- Fresh-clone build verification
- Release configuration verification
- Automated regression coverage
- Performance profiling and optimization
- One-hour and eight-hour stability tests
- Windows 10 and Windows 11 verification
- Installer/uninstaller
- Code signing
- Application updates
- Privacy, user, and troubleshooting documentation
- Accessibility and UX polish
- Release acceptance testing

## Progress Baseline

**96% is the synchronized overall product baseline as of 2026-07-31.** Future progress must use this roadmap and the implementation tracker together and must not move backward unless completed scope is explicitly reopened or proven incomplete and the reason is documented.

## Product Rule

Sentinel must investigate before acting, prefer silent monitoring when the system is healthy, request user involvement only when necessary, verify system-changing outcomes, and ensure AI assistance remains grounded in verified Sentinel evidence, history, and remediation state.
