# SAI-007 — Chat Continuation Guide
Version: 1.0
Status: Active
Last Updated: 2026-07-28

---

# Purpose

This document enables future development sessions to continue work on Sentinel AI efficiently and consistently.

It summarizes the project's current state, guiding principles, and expected workflow.

Before beginning work, review:

- SAI-001 — Project Constitution
- SAI-002 — Development Rules
- SAI-003 — Software Architecture
- SAI-004 — Sprint History
- SAI-005 — Product Roadmap
- SAI-006 — Coding Standards

These documents together define the project.

---

# Project Summary

Project Name

Sentinel AI

Platform

Windows Desktop

Framework

WinUI 3

Language

C#

Runtime

.NET 8

IDE

Visual Studio 2026

Version Control

Git

Repository

GitHub

Primary Branch

main

---

# Current Status

Current Version

0.2.0

Current Sprint

Sprint 2.1

Current Objective

Implement live system monitoring beginning with CPU utilization, followed by memory, disk, and network monitoring.

---

# Development Philosophy

The project should remain in a buildable state.

Features are developed vertically:

Plan

↓

Implement

↓

Build

↓

Run

↓

Verify

↓

Commit

↓

Push

↓

Update Documentation

---

# AI Responsibilities

When assisting with Sentinel AI:

- Prefer complete file replacements when practical.
- Explain architectural decisions before implementation.
- Preserve project organization.
- Avoid unnecessary complexity.
- Use modern .NET and Windows APIs.
- Keep the UI responsive.
- Update documentation after major milestones.

---

# User Responsibilities

The Product Owner provides:

- Product vision
- Feature priorities
- Acceptance testing
- Final approval

---

# Repository Structure

docs/

assets/

installer/

src/

tests/

---

# Project Structure

Current

Sentinel.App

Future

Sentinel.Core

Sentinel.Services

Sentinel.Security

Sentinel.AI

Sentinel.Data

Sentinel.Tests

---

# Immediate Priorities

1. Implement live CPU utilization.
2. Implement live memory monitoring.
3. Implement disk monitoring.
4. Implement network monitoring.
5. Refactor monitoring logic into Services.
6. Introduce ViewModels as the application grows.
7. Expand automated testing.

---

# Definition of Done

A task is complete only when:

- It builds successfully.
- It runs successfully.
- It satisfies the acceptance criteria.
- Documentation is updated.
- Changes are committed.
- Changes are pushed to GitHub.

---

# Before Ending a Development Session

Confirm:

✔ Build succeeded

✔ Application launched successfully

✔ Sprint History updated

✔ Documentation updated

✔ Git commit completed

✔ GitHub push completed

---

# Starting a New Chat

When beginning a new ChatGPT conversation:

1. Provide the current project folder or repository.
2. Mention the current sprint and version.
3. Share any build errors or blockers.
4. Reference this document if needed.

The assistant should review the documentation, identify the next unfinished roadmap item, and continue development without redoing completed work.

---

End of Document