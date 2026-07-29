# SAI-008 — Release Checklist

Version: 1.1

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This checklist defines the minimum requirements for completing a development session, feature, sprint, or release.

A task is **not considered complete** until all applicable checklist items have been completed.

This checklist also serves as the mandatory quality gate before any code is committed or released.

---

# Phase 1 — Planning

☐ Requirement understood

☐ Architecture reviewed (if needed)

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

☐ Temporary/debug code removed

☐ Placeholder implementations documented (if intentionally retained)

---

# Phase 3 — Build

☐ Solution builds successfully

☐ No build errors

☐ No unexpected warnings introduced

☐ All projects build successfully

☐ Build configuration verified (Debug/Release)

---

# Phase 4 — Verification

☐ Application launches successfully

☐ Feature behaves as expected

☐ Existing functionality still works

☐ No obvious regressions discovered

☐ Manual smoke test completed

☐ Performance acceptable

☐ UI verified (if applicable)

☐ Error scenarios tested

---

# Phase 5 — Documentation

Update documentation only if affected.

☐ CHANGELOG.md

☐ SAI-000_Project_Status.md

☐ SAI-004_Sprint_History.md

☐ PRODUCT_REQUIREMENTS.md (if requirements changed)

☐ README.md (if user-visible capabilities changed)

☐ Architecture documentation updated

☐ API documentation updated (if applicable)

☐ Other SAI documents updated (if architecture, standards, roadmap, or process changed)

---

# Phase 6 — Version Control

☐ Files reviewed

☐ Unused files removed

☐ Meaningful commit message written

☐ Commit created

☐ Changes pushed to GitHub

☐ Branch synchronized

☐ Repository builds from latest commit

---

# Phase 7 — Completion

☐ Acceptance criteria satisfied

☐ Technical debt documented

☐ Known issues documented

☐ Ready for next sprint

☐ Checklist completed

---

# Definition of Done

A feature is complete only when:

✓ Requirements satisfied

✓ Code complete

✓ Architecture maintained

✓ Project builds successfully

✓ Application runs successfully

✓ Feature verified

✓ Existing functionality verified

✓ Documentation updated

✓ No critical warnings

✓ Commit created

✓ Changes pushed

✓ Ready for next task

---

# Sprint Review

Before closing a sprint, confirm:

☐ What was completed?

☐ What remains?

☐ Any blockers?

☐ Any architectural changes?

☐ Documentation updated?

☐ Repository clean?

☐ Ready to continue?

---

End of Document