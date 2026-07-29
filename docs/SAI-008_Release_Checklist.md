# SAI-008 — Release Checklist

Version: 1.1

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This checklist defines the minimum requirements for completing a development session, feature, sprint, or release.

A task is **not considered complete** until all applicable checklist items have been completed.

This checklist also serves as the mandatory quality gate before code is committed or released.

---

# Phase 1 — Planning

☐ Requirement understood

☐ Architecture reviewed when needed

☐ Existing documentation reviewed

☐ Impact on existing features considered

☐ Dependencies identified

☐ Risks identified

☐ Implementation plan completed

---

# Phase 2 — Development

☐ Code implemented

☐ Code follows SAI-006 Coding Standards

☐ No unnecessary complexity introduced

☐ Solution fits project architecture

☐ Error handling implemented

☐ Logging added where appropriate

☐ Temporary or debug code removed

☐ Placeholder implementations documented when intentionally retained

---

# Phase 3 — Build

☐ Solution builds successfully

☐ No build errors

☐ No unexpected warnings introduced

☐ All affected projects build successfully

☐ Build configuration verified when applicable

---

# Phase 4 — Verification

☐ Application launches successfully

☐ Feature behaves as expected

☐ Existing functionality still works

☐ No obvious regressions discovered

☐ Manual smoke test completed

☐ Performance acceptable

☐ UI verified when applicable

☐ Error scenarios tested

---

# Phase 5 — Documentation

Update documentation only when affected.

☐ CHANGELOG.md

☐ SAI-000_Project_Status.md

☐ SAI-004_Sprint_History.md

☐ PRODUCT_REQUIREMENTS.md when requirements change

☐ README.md when user-visible capabilities change

☐ Architecture documentation updated

☐ API documentation updated when applicable

☐ Other SAI documents updated when architecture, standards, roadmap, or process changes

---

# Phase 6 — Version Control

☐ Files reviewed

☐ Unused files removed

☐ Meaningful commit message written

☐ Commit created

☐ Changes pushed to GitHub

☐ Correct production branch used

☐ Branch synchronized

☐ Repository builds from latest commit

---

# Phase 7 — Completion

☐ Acceptance criteria satisfied

☐ Technical debt documented

☐ Known issues documented

☐ Ready for next sprint or task

☐ Checklist completed

---

# Sprint 3 — Native Windows Monitoring Verification

The following items record the verified Sprint 3 system-monitoring milestone.

## Project Configuration

☑ Microsoft.Windows.CsWin32 package present

☑ Unsafe blocks enabled for generated pointer-based API signatures

☑ `NativeMethods.txt` created in the WinUI application project

☑ `GetSystemTimes` included in `NativeMethods.txt`

☑ `GlobalMemoryStatusEx` included in `NativeMethods.txt`

## SystemMonitor Production Implementation

☑ Placeholder and random CPU values removed

☑ CPU monitoring uses CsWin32-generated `PInvoke.GetSystemTimes`

☑ CPU usage calculated from consecutive idle, kernel, and user samples

☑ First CPU sample returns zero

☑ Invalid or reversed samples handled gracefully

☑ CPU result clamped to 0–100 percent

☑ Physical-memory monitoring uses CsWin32-generated `PInvoke.GlobalMemoryStatusEx`

☑ Used physical memory reported

☑ Total physical memory reported

☑ Physical-memory usage percentage reported

☑ Win32 failures handled without application termination

☑ Existing `SystemMonitor` public interface preserved for repository call sites

☑ No placeholder or random values remain in production `SystemMonitor`

## Integration and Runtime Verification

☑ MonitoringEngine receives native CPU values

☑ MonitoringEngine receives native physical-memory values

☑ Dashboard displays real CPU utilization

☑ Dashboard displays real used physical memory

☑ Dashboard displays real total physical memory

☑ Dashboard displays physical-memory percentage

☑ Dashboard refreshes once per second

☑ Dashboard timestamp updates correctly

☑ Solution build completed successfully

☑ Application launched successfully

☑ Runtime behavior verified by the product owner

☑ README updated for the verified Sprint 3 milestone

## Remaining Monitoring Work

☐ Production disk monitoring

☐ Network download throughput

☐ Network upload throughput

☐ Process statistics and intelligence

☐ Microsoft Defender status

☐ Windows Firewall status

☐ Monitoring integration tests

☐ Error-path and unavailable-service testing

---

# Definition of Done

A feature is complete only when:

✓ Requirements are satisfied

✓ Code is complete

✓ Architecture is maintained

✓ Project builds successfully

✓ Application runs successfully

✓ Feature is verified

✓ Existing functionality is verified

✓ Documentation is updated

✓ No critical warnings remain

✓ Commit is created

✓ Changes are pushed

✓ Work is ready for the next task

---

# Sprint Review

Before closing a sprint, confirm:

☐ What was completed?

☐ What remains?

☐ Are there any blockers?

☐ Were there architectural changes?

☐ Is documentation current?

☐ Is the repository clean?

☐ Is the production branch synchronized?

☐ Is the project ready to continue?

---

End of Document
