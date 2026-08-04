# SAI-034 — Phase 8 Final Product Acceptance

Version: 1.0

Status: Active — Final Release Gate

Last Updated: 2026-08-03

Copyright (c) 2026 Modern Methods.

---

# Purpose

Define the final runtime acceptance gate for Sentinel AI Phase 8 item 8 and commercial release readiness.

# Evidence Already Accepted

The following requirements are already satisfied by verified evidence and do not need to be repeated unless a regression appears:

- [x] One-hour stability test — PASS.
- [x] Eight-hour stability test — PASS; 480.19 minutes with bounded resource growth and no process failure.
- [x] Phase 8.4 process containment acceptance — PASS.
- [x] Phase 8.4 outbound firewall containment and reversal acceptance — PASS.
- [x] Phase 8.4 quarantine and restore acceptance — PASS.
- [x] Clean-VM reboot/startup verification — Sentinel starts automatically at sign-in and remains tray-only.
- [x] Release | x64 builds have repeatedly completed successfully after current feature work.

# Final Candidate Runtime Acceptance

Perform the remaining checks against the current `main` branch and the production Release | x64 package.

## 1. Normal / False-Positive Behavior

- [ ] Start Sentinel on a healthy Windows system.
- [ ] Confirm the normal surface remains quiet and does not expose technical warnings that require no user action.
- [ ] Browse normally, including common HTTPS traffic and routine background Windows activity.
- [ ] If VMware/VPN software is installed, allow normal VM/VPN traffic to run.
- [ ] Confirm unfamiliar traffic by itself does not produce a malicious/intrusion conclusion.
- [ ] Confirm Sentinel does not recommend blocking routine benign traffic without corroborating evidence.

PASS condition: normal activity remains quiet unless meaningful corroborated evidence exists.

## 2. Monitoring Continuity and Network Recovery

- [ ] Confirm Sentinel is running and monitoring normally.
- [ ] Temporarily disconnect the active network connection.
- [ ] Confirm Sentinel remains responsive and does not crash or hang.
- [ ] Reconnect the network.
- [ ] Confirm network evidence resumes automatically without restarting Sentinel.
- [ ] Confirm protection-health state returns to normal after connectivity is restored.

PASS condition: temporary network loss does not destabilize Sentinel and monitoring recovers automatically.

## 3. Sleep / Wake Recovery

- [ ] With Sentinel running, put Windows to sleep.
- [ ] Wake and sign back in.
- [ ] Confirm Sentinel remains running or resumes normally.
- [ ] Confirm CPU, memory, process, Defender/firewall, and network evidence refresh again.
- [ ] Confirm the app remains responsive and tray behavior remains correct.

PASS condition: Sentinel continues/recoveries normally after sleep/wake with no manual restart.

## 4. Protection / Containment Regression

Run from repository root:

`./tools/Run-Phase8ContainmentAcceptance.ps1`

- [x] Process containment — PASS on 2026-08-03.
- [x] Firewall block and verified reversal — PASS on 2026-08-03.
- [x] Quarantine and verified restore — PASS on 2026-08-03.

Repeat only if final release changes touch containment/remediation code.

## 5. Clean Production Install

Use a clean Windows VM or equivalent clean test machine.

- [ ] Install the production Sentinel AI package using the intended customer-facing installer/package.
- [ ] Installer/package identity displays **Sentinel AI** only.
- [ ] Sentinel shield artwork appears where supported during install and in installed application identity.
- [ ] No developer-facing `(Package)` suffix or project/debug naming is visible to the customer.
- [ ] Launch Sentinel from the installed application entry.
- [ ] Confirm first-run startup succeeds without Visual Studio, SDK tooling, or manual dependency repair.
- [ ] Confirm tray icon is present and the main window opens correctly when selected.
- [ ] Confirm settings window and dialogs fit correctly without clipped text.

PASS condition: a clean Windows user can install and launch Sentinel without development tooling or manual repair.

## 6. Reboot / Startup-to-Tray

- [ ] With the installed production package configured to start with Windows, reboot the clean VM.
- [ ] Sign in normally.
- [ ] Confirm Sentinel starts automatically.
- [ ] Confirm it remains tray-only unless the user opens it.
- [ ] Confirm monitoring becomes active after sign-in.

Existing clean-VM startup evidence already passed; repeat only against the final production installer if packaging changed since that verification.

## 7. Clean Uninstall

- [ ] Uninstall Sentinel AI using the normal Windows installed-app removal flow.
- [ ] Confirm application binaries/package are removed.
- [ ] Confirm Sentinel no longer starts at sign-in.
- [ ] Confirm no broken Start menu entry or active tray process remains.
- [ ] Preserve or remove user data only according to the documented uninstall policy.

PASS condition: uninstall leaves no active application component or broken startup entry.

## 8. Final Smoke / UX Check

- [ ] Open the main window and verify greeting, healthy-state language, monitoring evidence, settings, technical-detail disclosure, Ask Sentinel, quarantine view, and maintenance/optimization surfaces.
- [ ] Confirm no important text is clipped in default window sizes.
- [ ] Confirm scrollbars appear where content can exceed available height.
- [ ] Confirm controls and checkboxes are visibly readable and usable.
- [ ] Confirm no unexpected crash, debug break, freeze, or material lag occurs during the final session.

# Final Acceptance Result

Phase 8 item 8 is **PASS** only after every remaining unchecked runtime item above has been verified against the final release candidate or explicitly covered by existing accepted evidence.

When this document passes:

- Phase 8 becomes **8 of 8 complete**.
- Sentinel AI planned product development becomes **complete**.
- The project may advance to final release packaging/signing/distribution without adding new feature scope.

---

End of Document