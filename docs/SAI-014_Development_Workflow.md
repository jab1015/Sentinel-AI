# SAI-014 — Development Workflow

Version: 1.0

Status: Active

Last Updated: 2026-07-29

Copyright (c) 2026 Modern Methods.

---

# Purpose

This document defines the standard development workflow for Sentinel AI.

The objective is to maximize development speed while maintaining production-quality code, documentation, and architecture.

---

# Development Philosophy

- Build commercial-quality software.
- Keep every sprint releasable.
- Verify before integrating.
- Prefer maintainability over shortcuts.
- Minimize technical debt.
- Document important architectural decisions.

---

# Standard Workflow

## Step 1 — Planning

- Review requirements.
- Review architecture.
- Identify dependencies.
- Define acceptance criteria.

---

## Step 2 — Implementation

The assistant provides only the required work items.

Example:

CREATE FILE

REPLACE FILE

DELETE FILE

BUILD

RUN

NEXT

Avoid unnecessary explanations unless requested.

---

## Step 3 — Build

Every significant change should be followed by:

- Build
- Resolve errors
- Repeat until clean

No new work begins until the solution builds successfully.

---

## Step 4 — Verification

After a successful build:

- Run the application.
- Verify affected functionality.
- Check for regressions.

---

## Step 5 — Documentation

Update documentation only when appropriate.

Typical updates include:

- CHANGELOG.md
- SAI-000_Project_Status.md
- SAI-004_Sprint_History.md

Architecture documents should only change when architecture changes.

---

## Step 6 — Version Control

Before committing:

- Review modified files.
- Remove temporary files.
- Verify build.
- Verify application launch.

Commit messages should clearly describe the completed work.

Example

Sprint 3: Implement native monitoring foundation

---

## Step 7 — Sprint Completion

A sprint is complete when:

- Acceptance criteria met
- Builds successfully
- Runs successfully
- Documentation updated
- Changes committed
- Changes pushed

---

# Coding Standards

- Complete file replacements preferred.
- Preserve project architecture.
- Avoid duplicate logic.
- Keep services independent.
- UI consumes models only.
- Monitoring Engine coordinates monitoring.

---

# Research Policy

Research is encouraged when:

- Windows APIs
- Native integrations
- Security features
- Microsoft guidance
- Performance-critical code

Verified implementations are preferred over assumptions.

---

# Quality Gates

Every feature should satisfy:

✓ Builds successfully

✓ Runs successfully

✓ Fits architecture

✓ No unnecessary complexity

✓ Documentation updated when required

✓ Ready for future expansion

---

# Long-Term Goal

Maintain a consistent, repeatable workflow that enables Sentinel AI to evolve into a production-quality Windows security platform while reducing rework and technical debt.

---

End of Document