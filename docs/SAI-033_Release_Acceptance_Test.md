# SAI-033 — Production Release Acceptance Test

Version: 1.0

Status: Active — Final Acceptance

Last Updated: 2026-08-02

Copyright (c) 2026 Modern Methods.

---

# Purpose

Define the final acceptance gate for Sentinel AI Phase 7 and the commercial release candidate.

# Acceptance Procedure

Perform this test against the current `main` branch using **Release | x64** unless another release architecture is specifically under test.

## Build and Startup

- [ ] Pull the current `main` branch successfully.
- [ ] Release build completes with zero build errors.
- [ ] Sentinel launches normally.
- [ ] First visible window appears without the previously repaired startup lag or startup/debugging failures.
- [ ] No `REGDB_E_CLASSNOTREG`, `ApplicationData.Current`, `XamlRoot`, or process-module enumeration startup failure occurs.

## Healthy-State Experience

- [ ] Personalized greeting persists correctly.
- [ ] Healthy system displays **Your computer is healthy.**
- [ ] Healthy system does not unnecessarily surface investigation warnings or demand action.
- [ ] Monitoring continues quietly.
- [ ] Technical details remain available through progressive disclosure.

## Monitoring Evidence

- [ ] CPU evidence populates.
- [ ] Memory evidence populates.
- [ ] Disk evidence populates.
- [ ] Network evidence populates.
- [ ] Process evidence populates.
- [ ] Defender status populates.
- [ ] Firewall status populates.
- [ ] Evidence refreshes without UI freezing or material lag.

## Investigation Safety

- [ ] Historical raw Windows errors do not independently force Action Required.
- [ ] Uncorrelated uncommon-port network activity does not independently trigger a block recommendation.
- [ ] Recurrence tracking counts distinct observations rather than every monitoring refresh.
- [ ] Sentinel does not make unsupported security claims.
- [ ] Investigation guidance explains verified findings and required action in nontechnical language.

## Ask Sentinel

- [ ] Ask Sentinel accepts a user question.
- [ ] Ask Sentinel remains grounded in verified local evidence.
- [ ] Unsupported conclusions are not invented when evidence is insufficient.
- [ ] Ask Sentinel interaction does not stop monitoring or degrade startup/runtime responsiveness.

## Remediation Safety

- [ ] Safe authorized remediation paths remain available when applicable.
- [ ] Approval-required actions still require user authorization.
- [ ] Verification-after-action behavior remains functional.
- [ ] Failed remediation leaves the application in a safe state and does not falsely report success.

## Accessibility and UX

- [ ] Keyboard navigation reaches interactive controls.
- [ ] Ask Sentinel can be submitted using the supported keyboard interaction.
- [ ] Technical details can be expanded/collapsed by keyboard.
- [ ] Screen-reader automation names/headings are exposed for primary controls and sections.
- [ ] Text remains readable and the executive experience remains uncluttered.

## Stability

Previously completed release evidence:

- [x] One-hour stability test — PASS.
- [x] Eight-hour stability test — PASS.

Final candidate check:

- [ ] No new crash, hang, runaway resource use, or material performance regression is observed during final acceptance smoke testing.

## Release Infrastructure

- [ ] Installer/uninstaller release configuration remains buildable.
- [ ] Code-signing boundary does not require secrets in source control.
- [ ] Application-update boundary requires trusted Windows signature/package verification.
- [ ] Privacy, user, and troubleshooting documentation is present.
- [ ] No private signing keys, passwords, tokens, or production credentials are committed to the repository.

# Acceptance Result

**PASS** only when every unchecked item above has been runtime verified for the release candidate or is explicitly satisfied by verified release evidence.

If any item fails, Phase 7 item 12 remains incomplete until the failure is repaired and retested.

When all items pass, Phase 7 is **12 of 12 complete** and the implementation tracker may be advanced to the completed commercial-release milestone.

---

End of Document
