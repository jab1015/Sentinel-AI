# SAI-008 — Release Checklist

Version: 1.4  
Status: Complete — Version 1.0.20.0 Acceptance Passed; Discovery 2.0 Development Acceptance Passed  
Last Updated: 2026-08-05

Copyright (c) 2026 Modern Methods.

---

# Purpose

This checklist defines the minimum requirements for completing a development session, feature, sprint, or release.

A task is complete only when applicable implementation, build, verification, documentation, and version-control requirements are satisfied.

# Version 1.0.20.0 Final Release Acceptance

## Planning and Development

☑ Product-wide Sentinel Discovery rule implemented  
☑ Investigation Engine integrated with proactive Discovery  
☑ Ask Sentinel grounded in verified evidence and investigation history  
☑ Quarantine Manager integrated with verified backend actions  
☑ Activity Center / Recent Activity persistence verified  
☑ Approval boundaries preserved for system-changing actions  
☑ Healthy evidence remains quiet  
☑ Initial Discovery state clearly communicates evidence gathering

## Acceptance Harnesses

☑ Sentinel Discovery Acceptance — 8/8 PASS  
☑ Quarantine Acceptance — 6/6 scenarios PASS  
☑ Approval gating verified  
☑ Quarantine/catalog registration and reconciliation verified  
☑ Restore/reversal verified  
☑ Permanent deletion/catalog cleanup verified

## Integrated Production Validation

☑ Application launches successfully  
☑ Initial Discovery/gathering state displays correctly  
☑ Verified actionable condition is surfaced proactively  
☑ Ask Sentinel agrees with active verified Discovery evidence  
☑ Authoritative investigation executes  
☑ Unverified automatic repair is refused safely  
☑ Source/confidence/trust behavior verified  
☑ Recent Activity persists verified investigation outcome  
☑ Quarantine Manager opens and reports current state correctly  
☑ Closing main window preserves background tray operation  
☑ Tray menu exposes Open Sentinel AI, Options, and Exit Sentinel AI

## Installed Release Validation

☑ Release package created successfully  
☑ `Sentinel.App (Package)_1.0.20.0_x64.msixbundle` installed successfully on VM  
☑ Installed application launches independently of Visual Studio  
☑ Installed Discovery completed successfully  
☑ VM Secure Boot-disabled condition surfaced proactively  
☑ Firmware configuration correctly remained a guided user action  
☑ Installed application remained running in system tray after main window closed  
☑ Windows reboot/sign-in completed  
☑ Sentinel started automatically after sign-in  
☑ Sentinel started tray-only without forcing the main window open

# Discovery 2.0 Development Acceptance

☑ Phase 1 — Persistent Investigation Intelligence complete  
☑ Persistent investigation acceptance — 6/6 PASS  
☑ Expanded persistent/presentation policy acceptance — 10/10 PASS  
☑ Phase 2 — Verified Persistent Exceptions complete  
☑ Incomplete and critical findings cannot be silenced  
☑ Eligible exhausted noncritical conditions can enter silent monitoring  
☑ Monitoring continues while notifications are suppressed  
☑ Phase 3 — Live Persistent Exception Integration complete  
☑ Live persistent exception acceptance — 5/5 PASS  
☑ Unrelated findings do not inherit persistent exceptions  
☑ Notifications can resume without disabling monitoring  
☑ Phase 4 — Cross-Investigation Correlation complete  
☑ Correlation acceptance — 7/7 PASS  
☑ Unsupported root-cause relationships are not asserted  
☑ Critical evidence retains priority  
☑ Phase 5 — Trusted Knowledge Engine complete  
☑ Trusted Knowledge acceptance — 8/8 PASS  
☑ Incomplete, critical, and low-confidence conclusions cannot become reusable trusted knowledge  
☑ Material evidence change invalidates prior reusable conclusions  
☑ Expired knowledge requires revalidation  
☑ Current critical evidence always requires direct investigation

## Discovery 2.0 Documentation

☑ SAI-025 Master Development Plan updated  
☑ SAI-013 Implementation Tracker updated  
☑ SAI-012 Product Roadmap updated  
☑ SAI-008 Release Checklist updated with Discovery 2.0 acceptance evidence

# Release Decision

**Version 1.0.20.0 baseline: PASS**

**Sentinel Discovery 2.0 development acceptance: PASS — 5/5 phases complete.**

Discovery 2.0 completion does not by itself constitute a newly packaged public release. Public distribution signing remains separate release work. The self-signed Modern Methods certificate is suitable for controlled testing but does not eliminate certificate trust prompts on unrelated customer computers.

---

End of Document
